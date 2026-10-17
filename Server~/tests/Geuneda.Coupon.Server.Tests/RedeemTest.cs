using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Geuneda.Coupon.Server.Tests
{
	public class RedeemTest
	{
		private static CouponCampaign Shared(string id = "welcome", string code = "WELCOME2026", string reward = "100", DateTimeOffset? startsAt = null,
			DateTimeOffset? endsAt = null, bool enabled = true, int? max = null, CouponConditions conditions = null)
		{
			return new CouponCampaign(id, CouponKind.Shared, code, reward, startsAt, endsAt, enabled, max, conditions);
		}

		private static CouponCampaign Unique(string id = "launch", string reward = "50", int? max = null)
		{
			return new CouponCampaign(id, CouponKind.Unique, reward: reward, maxRedemptions: max);
		}

		[Fact]
		public async Task Shared_RedeemsOncePerAccount_AndReturnsTheFirstGrantAgain()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared());

			var (http, first) = await server.RedeemAsync("alice", "welcome-2026 ");

			Assert.Equal(HttpStatusCode.OK, http.StatusCode);
			Assert.Equal(0, first.Code);
			Assert.Equal("welcome", first.CampaignId);
			Assert.Equal("100", first.Grant.Value.GetProperty("gems").GetString());
			Assert.Equal(CouponTestServer.Start, first.RedeemedAt);

			server.Time.Advance(TimeSpan.FromMinutes(5));
			var (_, again) = await server.RedeemAsync("alice", "WELCOME2026");

			Assert.Equal(CouponResultCodes.AlreadyRedeemed, again.Code);
			Assert.Equal("welcome", again.CampaignId);
			Assert.Equal("alice", again.Grant.Value.GetProperty("account").GetString());
			Assert.Equal(CouponTestServer.Start, again.RedeemedAt);
			Assert.Single(server.Grantor.Calls);

			var (_, bob) = await server.RedeemAsync("bob", "welcome2026");
			Assert.Equal(0, bob.Code);
			Assert.Equal(2, server.Store.GetRedemptionCount("welcome"));
			Assert.Equal(1, server.Logs.Count(LogLevel.Information, "Redeemed welcome code WELCOME2026 for account bob"));
		}

		[Fact]
		public async Task Grantor_ReceivesCampaignCodeAndContext()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared());

			await server.RedeemAsync("alice", "welcome2026", "IOS", " 1.2.3 ", "kr");

			var grant = Assert.Single(server.Grantor.Calls);
			Assert.Equal("welcome", grant.Campaign.Id);
			Assert.Equal("100", grant.Campaign.Reward);
			Assert.Equal("WELCOME2026", grant.Code);
			Assert.Equal("alice", grant.AccountId);
			Assert.Equal("ios", grant.Context.Platform);
			Assert.Equal("1.2.3", grant.Context.AppVersion);
			Assert.Equal("KR", grant.Context.Country);
			Assert.Equal(CouponTestServer.Start, grant.RedeemedAt);
		}

		[Fact]
		public async Task UnknownOrMalformedCode_IsInvalid()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared());

			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("alice", "NOPE1234")).Body.Code);
			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("alice", "abc")).Body.Code);
			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("alice", "WELCOME_2026")).Body.Code);
			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("alice", "")).Body.Code);
			Assert.Empty(server.Grantor.Calls);
		}

		[Fact]
		public async Task Period_And_Enabled_AreChecked_ButAnEarlierRedeemerStillGetsItsGrant()
		{
			await using var server = await CouponTestServer.StartAsync();
			var start = CouponTestServer.Start.AddHours(1);
			await server.AddAsync(Shared(startsAt: start, endsAt: start.AddHours(1)));

			Assert.Equal(CouponResultCodes.NotStarted, (await server.RedeemAsync("alice", "WELCOME2026")).Body.Code);

			server.Time.Advance(TimeSpan.FromHours(1));
			Assert.Equal(0, (await server.RedeemAsync("alice", "WELCOME2026")).Body.Code);

			server.Time.Advance(TimeSpan.FromHours(1));
			Assert.Equal(CouponResultCodes.Expired, (await server.RedeemAsync("bob", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("alice", "WELCOME2026")).Body.Code);

			Assert.True(await server.AdminAsync(admin => admin.UpdateCampaignAsync(Shared(enabled: false))));
			Assert.Equal(CouponResultCodes.Disabled, (await server.RedeemAsync("carol", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("alice", "WELCOME2026")).Body.Code);

			Assert.True(await server.AdminAsync(admin => admin.UpdateCampaignAsync(Shared())));
			Assert.Equal(0, (await server.RedeemAsync("carol", "WELCOME2026")).Body.Code);
		}

		[Fact]
		public async Task UniqueCodes_AreSingleUse_AndOnePerAccountPerCampaign()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Unique(), "LAUNCH-AAAA-0001", "launch-aaaa-0002");

			Assert.Equal(0, (await server.RedeemAsync("alice", "LAUNCHAAAA0001")).Body.Code);
			Assert.Equal(CouponResultCodes.CodeUsed, (await server.RedeemAsync("bob", "launch-aaaa-0001")).Body.Code);

			var second = (await server.RedeemAsync("alice", "LAUNCHAAAA0002")).Body;
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, second.Code);
			Assert.Equal("alice", second.Grant.Value.GetProperty("account").GetString());

			Assert.Equal(0, (await server.RedeemAsync("bob", "LAUNCHAAAA0002")).Body.Code);
			Assert.Equal(2, server.Grantor.Calls.Count);
		}

		[Fact]
		public async Task MaxRedemptions_SellsOut()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared(max: 2));

			Assert.Equal(0, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
			Assert.Equal(0, (await server.RedeemAsync("b", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.SoldOut, (await server.RedeemAsync("c", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);

			Assert.True(await server.AdminAsync(admin => admin.UpdateCampaignAsync(Shared(max: 3))));
			Assert.Equal(0, (await server.RedeemAsync("c", "WELCOME2026")).Body.Code);
		}

		[Fact]
		public async Task Conditions_FilterPlatformCountryAndAppVersion()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared(conditions: new CouponConditions(new[] { "ios" }, new[] { "kr", "JP" }, "1.10.0")));

			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "android", "1.10.0", "KR")).Body.Code);
			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "ios", "1.10.0", "US")).Body.Code);
			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "ios", "1.10.0", null)).Body.Code);
			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "ios", "1.9.9", "KR")).Body.Code);
			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "ios", "1.10.0-beta", "KR")).Body.Code);
			Assert.Equal(CouponResultCodes.NotEligible, (await server.RedeemAsync("a", "WELCOME2026", "ios", null, "KR")).Body.Code);
			Assert.Equal(0, (await server.RedeemAsync("a", "WELCOME2026", "IOS", "1.10", "jp")).Body.Code);
			Assert.Equal(0, (await server.RedeemAsync("b", "WELCOME2026", "ios", "2.0.0.1", "kr")).Body.Code);
		}

		[Fact]
		public async Task ContextResolver_ReplacesReportedValues()
		{
			await using var server = await CouponTestServer.StartAsync(configure: coupon => coupon.AddContextResolver<HeaderCountryResolver>());
			await server.AddAsync(Shared(conditions: new CouponConditions(countries: new[] { "KR" })));

			var reportedKr = await server.PostAsync("a", new CouponRedeemRequest { Code = "WELCOME2026", Country = "KR" }, "X-Country", "US");
			Assert.Contains($"\"code\":{CouponResultCodes.NotEligible}", await reportedKr.Content.ReadAsStringAsync());

			var resolvedKr = await server.PostAsync("a", new CouponRedeemRequest { Code = "WELCOME2026", Country = "US" }, "X-Country", "kr");
			Assert.Contains("\"code\":0", await resolvedKr.Content.ReadAsStringAsync());
		}

		[Fact]
		public async Task ContextResolver_ChangingTheAccount_Is500()
		{
			await using var server = await CouponTestServer.StartAsync(configure: coupon => coupon.AddContextResolver<AccountChangingResolver>());
			await server.AddAsync(Shared());

			Assert.Equal(HttpStatusCode.InternalServerError, (await server.PostAsync("a", new CouponRedeemRequest { Code = "WELCOME2026" })).StatusCode);
			Assert.Empty(server.Grantor.Calls);
		}

		[Fact]
		public async Task Grantor_RejectedOrDeferred_IsNotRecorded()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Unique(max: 1), "UNIQUE0001");

			server.Grantor.Next = _ => CouponGrantResult.Rejected("level too low");
			var rejected = (await server.RedeemAsync("a", "UNIQUE0001")).Body;
			Assert.Equal(CouponResultCodes.GrantRejected, rejected.Code);
			Assert.Equal("level too low", rejected.Message);

			server.Grantor.Next = _ => CouponGrantResult.RetryLater("maintenance");
			Assert.Equal(CouponResultCodes.GrantDeferred, (await server.RedeemAsync("a", "UNIQUE0001")).Body.Code);
			Assert.Equal(0, server.Store.GetRedemptionCount("launch"));

			server.Grantor.Next = null;
			Assert.Equal(0, (await server.RedeemAsync("a", "UNIQUE0001")).Body.Code);
			Assert.Equal(1, server.Store.GetRedemptionCount("launch"));
		}

		[Fact]
		public async Task Grantor_ExceptionOrUndefinedResult_Is500AndNotRecorded()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Unique(), "UNIQUE0001");

			server.Grantor.Throw = new InvalidOperationException("db down");
			Assert.Equal(HttpStatusCode.InternalServerError, (await server.PostAsync("a", new CouponRedeemRequest { Code = "UNIQUE0001" })).StatusCode);

			server.Grantor.Throw = null;
			server.Grantor.Next = _ => default;
			Assert.Equal(HttpStatusCode.InternalServerError, (await server.PostAsync("a", new CouponRedeemRequest { Code = "UNIQUE0001" })).StatusCode);

			server.Grantor.Next = null;
			Assert.Equal(0, (await server.RedeemAsync("b", "UNIQUE0001")).Body.Code);
		}

		[Fact]
		public async Task Grantor_WithoutResponse_RedeemsWithoutGrant()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared());
			server.Grantor.Next = _ => CouponGrantResult.Granted();

			var (http, body) = await server.RedeemAsync("a", "WELCOME2026");

			Assert.Equal(0, body.Code);
			Assert.Null(body.Grant);
			Assert.DoesNotContain("\"grant\"", await http.Content.ReadAsStringAsync());
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
		}

		[Fact]
		public async Task CodeFailures_LockTheAccount_UntilTheWindowPasses()
		{
			await using var server = await CouponTestServer.StartAsync(options => options.MaxFailures = 3);
			await server.AddAsync(Shared());
			await server.AddAsync(Shared("expired", "EXPIRED2020", endsAt: CouponTestServer.Start));

			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("a", "WRONG0001")).Body.Code);
			server.Time.Advance(TimeSpan.FromMinutes(10));
			Assert.Equal(CouponResultCodes.Expired, (await server.RedeemAsync("a", "EXPIRED2020")).Body.Code);
			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("a", "x")).Body.Code);

			var (http, locked) = await server.RedeemAsync("a", "WELCOME2026");

			Assert.Equal((HttpStatusCode) 429, http.StatusCode);
			Assert.Equal(CouponResultCodes.TooManyAttempts, locked.Code);
			Assert.Equal(3001, locked.RetryAfterSeconds);
			Assert.Equal(TimeSpan.FromSeconds(3001), http.Headers.RetryAfter.Delta);
			Assert.Equal(1, server.Logs.Count(LogLevel.Warning, "Rejected coupon redemption for account a: 3 failed or in-flight codes"));
			Assert.Empty(server.Grantor.Calls);

			// 다른 계정은 막히지 않는다
			Assert.Equal(0, (await server.RedeemAsync("b", "WELCOME2026")).Body.Code);

			// 잠긴 동안의 요청은 세지 않는다. 가장 오래된 실패가 창 밖으로 나가면 풀린다
			server.Time.Advance(TimeSpan.FromMinutes(50));
			Assert.Equal(CouponResultCodes.TooManyAttempts, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
			server.Time.Advance(TimeSpan.FromSeconds(1));
			Assert.Equal(0, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
		}

		[Fact]
		public async Task ConcurrentFailures_CannotPassTheLimitTogether()
		{
			await using var server = await CouponTestServer.StartAsync(options => options.MaxFailures = 3, coupon =>
			{
				coupon.Services.AddSingleton<GatedCouponStore>();
				coupon.Services.Replace(ServiceDescriptor.Singleton<ICouponStore>(provider => provider.GetRequiredService<GatedCouponStore>()));
			});
			var store = server.Services.GetRequiredService<GatedCouponStore>();

			// 코드 찾기를 붙잡은 채 실패할 요청 10개를 함께 보낸다. 확인과 기록이 따로면 10개 모두 판정에 들어간다
			var requests = Enumerable.Range(0, 10).Select(i => server.RedeemAsync("a", "WRONG000" + i)).ToArray();

			for (var i = 0; i < 100 && store.Lookups < 3; i++)
			{
				await Task.Delay(20);
			}

			await Task.Delay(200);
			Assert.Equal(3, store.Lookups);
			store.Gate.SetResult();

			var codes = (await Task.WhenAll(requests)).Select(result => result.Body.Code).ToArray();
			Assert.Equal(3, codes.Count(code => code == CouponResultCodes.InvalidCode));
			Assert.Equal(7, codes.Count(code => code == CouponResultCodes.TooManyAttempts));
			Assert.Equal(3, store.Lookups);
		}

		[Fact]
		public async Task InFlightSuccess_DoesNotLeaveAFalseLockWarning()
		{
			// 성공할 요청이 예약을 쥔 채 기다리는 동안 실패 요청이 끝나고, 그 뒤 성공 요청이 예약을 푼다. 실패는 1번뿐이라 잠기지 않는다
			await using var server = await CouponTestServer.StartAsync(options => options.MaxFailures = 2);
			await server.AddAsync(Shared());
			server.Grantor.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			var success = server.RedeemAsync("a", "WELCOME2026");

			for (var i = 0; i < 100 && server.Grantor.Calls.IsEmpty; i++)
			{
				await Task.Delay(20);
			}

			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("a", "WRONG0001")).Body.Code);
			server.Grantor.Gate.SetResult();
			Assert.Equal(0, (await success).Body.Code);

			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("a", "WRONG0002")).Body.Code);
			Assert.Equal(0, server.Logs.Count(LogLevel.Warning, "Rejected coupon redemption"));
			Assert.Equal(CouponResultCodes.TooManyAttempts, (await server.RedeemAsync("a", "WRONG0003")).Body.Code);
		}

		[Fact]
		public async Task ExceptionsAndSuccesses_ReleaseTheirReservation()
		{
			await using var server = await CouponTestServer.StartAsync(options => options.MaxFailures = 1);
			await server.AddAsync(Shared());

			server.Grantor.Throw = new InvalidOperationException("db down");
			Assert.Equal(HttpStatusCode.InternalServerError, (await server.PostAsync("a", new CouponRedeemRequest { Code = "WELCOME2026" })).StatusCode);

			server.Grantor.Throw = null;
			Assert.Equal(0, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
			Assert.Equal(CouponResultCodes.InvalidCode, (await server.RedeemAsync("a", "WRONG0001")).Body.Code);
			Assert.Equal(CouponResultCodes.TooManyAttempts, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
		}

		[Fact]
		public async Task NotCodeFailures_DoNotCount()
		{
			await using var server = await CouponTestServer.StartAsync(options => options.MaxFailures = 2);
			await server.AddAsync(Shared());
			await server.AddAsync(Shared("other", "OTHER2026"));

			Assert.Equal(0, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);

			for (var i = 0; i < 3; i++)
			{
				Assert.Equal(CouponResultCodes.AlreadyRedeemed, (await server.RedeemAsync("a", "WELCOME2026")).Body.Code);
			}

			server.Grantor.Next = _ => CouponGrantResult.Rejected();

			for (var i = 0; i < 3; i++)
			{
				Assert.Equal(CouponResultCodes.GrantRejected, (await server.RedeemAsync("a", "OTHER2026")).Body.Code);
			}

			Assert.Equal(400, (int) (await server.PostAsync("a", new { platform = "ios" })).StatusCode);
			server.Grantor.Next = null;
			Assert.Equal(0, (await server.RedeemAsync("a", "OTHER2026")).Body.Code);
		}

		[Fact]
		public async Task Requests_AreValidated()
		{
			await using var server = await CouponTestServer.StartAsync(options => options.MaxRequestBytes = 256);
			await server.AddAsync(Shared());

			var anonymous = await server.RedeemAsync(null, "WELCOME2026");
			Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Http.StatusCode);
			Assert.Equal(CouponResultCodes.Unauthorized, anonymous.Body.Code);

			async Task<HttpResponseMessage> Raw(string content, string mediaType)
			{
				var request = new HttpRequestMessage(HttpMethod.Post, "coupons/redeem") { Content = new StringContent(content, Encoding.UTF8, mediaType) };
				request.Headers.Add("X-Account", "a");
				return await server.Http.SendAsync(request);
			}

			var plain = await Raw("{\"code\":\"WELCOME2026\"}", "text/plain");
			Assert.Equal(HttpStatusCode.UnsupportedMediaType, plain.StatusCode);
			Assert.Contains($"\"code\":{CouponResultCodes.BadRequest}", await plain.Content.ReadAsStringAsync());

			Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Raw("{\"code\":\"" + new string('A', 300) + "\"}", "application/json")).StatusCode);
			Assert.Equal(HttpStatusCode.BadRequest, (await Raw("{\"code\":", "application/json")).StatusCode);
			Assert.Equal(HttpStatusCode.BadRequest, (await Raw("null", "application/json")).StatusCode);
			Assert.Equal(HttpStatusCode.BadRequest, (await Raw("{\"code\":5}", "application/json")).StatusCode);
			Assert.Equal(HttpStatusCode.OK, (await Raw("{\"CODE\":\"WELCOME2026\",\"extra\":1}", "application/json")).StatusCode);
		}

		[Fact]
		public async Task ConcurrentRequests_GrantOnce()
		{
			await using var server = await CouponTestServer.StartAsync();
			await server.AddAsync(Shared());
			await server.AddAsync(Unique(), "UNIQUE0001");
			server.Grantor.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			var sameAccount = new[] { server.RedeemAsync("a", "WELCOME2026"), server.RedeemAsync("a", "WELCOME2026") };
			var sameCode = new[] { server.RedeemAsync("b", "UNIQUE0001"), server.RedeemAsync("c", "UNIQUE0001") };
			await Task.Delay(200);
			server.Grantor.Gate.SetResult();

			var accountCodes = (await Task.WhenAll(sameAccount)).Select(result => result.Body.Code.Value).OrderBy(code => code).ToArray();
			var codeCodes = (await Task.WhenAll(sameCode)).Select(result => result.Body.Code.Value).OrderBy(code => code).ToArray();

			Assert.Equal(new[] { 0, CouponResultCodes.AlreadyRedeemed }, accountCodes);
			Assert.Equal(new[] { 0, CouponResultCodes.CodeUsed }, codeCodes);
			Assert.Equal(2, server.Grantor.Calls.Count);
		}
	}
}
