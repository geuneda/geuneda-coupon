using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Geuneda.Coupon.Server.Tests
{
	/// <summary>
	/// 127.0.0.1 빈 포트에 띄운 쿠폰 서버입니다. 계정은 <c>X-Account</c> 헤더, 저장소는 메모리, 지급자는 <see cref="TestGrantor"/>, 시간은 <see cref="Time"/> 으로 움직입니다.
	/// </summary>
	internal sealed class CouponTestServer : IAsyncDisposable
	{
		public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

		private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

		private readonly WebApplication _app;

		private CouponTestServer(WebApplication app, FakeTimeProvider time, ListLoggerProvider logs, Uri address)
		{
			_app = app;
			Time = time;
			Logs = logs;
			Http = new HttpClient { BaseAddress = address };
		}

		public FakeTimeProvider Time { get; }
		public ListLoggerProvider Logs { get; }
		public HttpClient Http { get; }
		public IServiceProvider Services => _app.Services;
		public TestGrantor Grantor => _app.Services.GetRequiredService<TestGrantor>();
		public InMemoryCouponStore Store => _app.Services.GetRequiredService<InMemoryCouponStore>();

		public static async Task<CouponTestServer> StartAsync(Action<CouponServerOptions> options = null, Action<CouponServerBuilder> configure = null)
		{
			var time = new FakeTimeProvider(Start);
			var logs = new ListLoggerProvider();
			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			builder.Logging.AddProvider(logs);
			builder.Services.AddSingleton<TimeProvider>(time);
			builder.Services.AddSingleton<TestGrantor>();
			var coupon = builder.Services.AddGeunedaCoupon(options).AddInMemoryStore();
			// 테스트가 대본을 바꾸는 인스턴스를 지급자로 쓴다(AddGrantor<T> 는 따로 인스턴스를 만듦)
			builder.Services.AddSingleton<ICouponGrantor>(provider => provider.GetRequiredService<TestGrantor>());
			configure?.Invoke(coupon);

			var app = builder.Build();
			app.MapGeunedaCoupon("/coupons", context =>
			{
				var account = context.Request.Headers["X-Account"].ToString();
				return Task.FromResult(string.IsNullOrEmpty(account) ? null : account);
			});
			await app.StartAsync();

			var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
			return new CouponTestServer(app, time, logs, new Uri(address + "/"));
		}

		/// <summary>운영 입구로 캠페인을 넣습니다.</summary>
		public async Task AddAsync(CouponCampaign campaign, params string[] codes)
		{
			using var scope = _app.Services.CreateScope();
			var admin = scope.ServiceProvider.GetRequiredService<CouponAdmin>();
			Assert.Equal(CouponAddResult.Added, await admin.AddCampaignAsync(campaign));

			if (codes.Length > 0)
			{
				Assert.Equal(codes.Length, (await admin.AddCodesAsync(campaign.Id, codes)).Added);
			}
		}

		public async Task<T> AdminAsync<T>(Func<CouponAdmin, Task<T>> action)
		{
			using var scope = _app.Services.CreateScope();
			return await action(scope.ServiceProvider.GetRequiredService<CouponAdmin>());
		}

		public Task<HttpResponseMessage> PostAsync(string account, object body, string headerName = null, string headerValue = null)
		{
			var request = new HttpRequestMessage(HttpMethod.Post, "coupons/redeem") { Content = JsonContent.Create(body, options: Json) };

			if (account != null)
			{
				request.Headers.Add("X-Account", account);
			}

			if (headerName != null)
			{
				request.Headers.Add(headerName, headerValue);
			}

			return Http.SendAsync(request);
		}

		/// <summary>코드를 보내고 응답 본문을 읽습니다.</summary>
		public async Task<(HttpResponseMessage Http, CouponRedeemResponse<JsonElement?> Body)> RedeemAsync(string account, string code,
			string platform = "android", string appVersion = "1.0.0", string country = null)
		{
			var response = await PostAsync(account, new CouponRedeemRequest { Code = code, Platform = platform, AppVersion = appVersion, Country = country });
			var body = await response.Content.ReadFromJsonAsync<CouponRedeemResponse<JsonElement?>>(Json);
			return (response, body);
		}

		public async ValueTask DisposeAsync()
		{
			Http.Dispose();
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}

	/// <summary>
	/// 대본 지급자입니다. <see cref="Next"/> 가 있으면 그 결과를, 없으면 <c>{"gems": 보상}</c> 으로 지급합니다. <see cref="Gate"/> 가 있으면 끝날 때까지 기다립니다.
	/// </summary>
	internal sealed class TestGrantor : ICouponGrantor
	{
		public readonly ConcurrentQueue<CouponGrant> Calls = new ConcurrentQueue<CouponGrant>();
		public Func<CouponGrant, CouponGrantResult> Next;
		public Exception Throw;
		public TaskCompletionSource Gate;

		public async Task<CouponGrantResult> GrantAsync(CouponGrant grant, CancellationToken cancellationToken)
		{
			Calls.Enqueue(grant);

			if (Gate != null)
			{
				await Gate.Task;
			}

			if (Throw != null)
			{
				throw Throw;
			}

			return Next != null ? Next(grant) : CouponGrantResult.Granted(new { gems = grant.Campaign.Reward, account = grant.AccountId });
		}
	}

	/// <summary>로그를 모읍니다.</summary>
	internal sealed class ListLoggerProvider : ILoggerProvider
	{
		public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new ConcurrentQueue<(LogLevel, string)>();

		public ILogger CreateLogger(string categoryName)
		{
			return new ListLogger(this);
		}

		public void Dispose()
		{
		}

		public int Count(LogLevel level, string contains)
		{
			return Entries.Count(entry => entry.Level == level && entry.Message.Contains(contains, StringComparison.Ordinal));
		}

		private sealed class ListLogger : ILogger
		{
			private readonly ListLoggerProvider _provider;

			public ListLogger(ListLoggerProvider provider)
			{
				_provider = provider;
			}

			public IDisposable BeginScope<TState>(TState state)
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
			{
				_provider.Entries.Enqueue((logLevel, formatter(state, exception)));
			}
		}
	}

	/// <summary>헤더 <c>X-Country</c> 로 국가를 정하는 해석기입니다.</summary>
	internal sealed class HeaderCountryResolver : ICouponContextResolver
	{
		public Task<CouponContext> ResolveAsync(HttpContext http, CouponContext reported, CancellationToken cancellationToken)
		{
			var country = http.Request.Headers["X-Country"].ToString();
			return Task.FromResult(new CouponContext(reported.AccountId, reported.Platform, reported.AppVersion, country));
		}
	}

	/// <summary>계정을 바꾸는 잘못된 해석기입니다.</summary>
	internal sealed class AccountChangingResolver : ICouponContextResolver
	{
		public Task<CouponContext> ResolveAsync(HttpContext http, CouponContext reported, CancellationToken cancellationToken)
		{
			return Task.FromResult(new CouponContext("someone-else", reported.Platform, reported.AppVersion, reported.Country));
		}
	}
}
