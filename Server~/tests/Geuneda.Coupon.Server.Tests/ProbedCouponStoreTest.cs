using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Geuneda.Coupon.Server.Tests
{
	/// <summary>시험용 저장소 데코레이터가 원래 등록의 수명과 해제를 그대로 두는지입니다.</summary>
	public class ProbedCouponStoreTest
	{
		[Fact]
		public async Task Decorate_TypeAndFactoryRegistrations_AreDisposedWithTheScope()
		{
			foreach (var register in new Action<CouponServerBuilder>[]
			         {
				         coupon => coupon.AddStore<DisposableStore>(),
				         coupon => coupon.Services.Replace(ServiceDescriptor.Scoped<ICouponStore>(provider => new DisposableStore(provider.GetRequiredService<DisposalLog>())))
			         })
			{
				var log = new DisposalLog();
				var probe = new RedeemProbe();
				var services = new ServiceCollection();
				services.AddSingleton(log);
				var coupon = services.AddGeunedaCoupon();
				register(coupon);
				ProbedCouponStore.Decorate(coupon, probe);

				await using (var provider = services.BuildServiceProvider())
				{
					await using (var scope = provider.CreateAsyncScope())
					{
						Assert.IsType<ProbedCouponStore>(scope.ServiceProvider.GetRequiredService<ICouponStore>());
						Assert.Equal(1, log.Created);
						Assert.Equal(0, log.Disposed);
					}

					Assert.Equal(1, log.Disposed);
				}
			}
		}

		[Fact]
		public async Task Decorate_InstanceRegistration_IsNotDisposed()
		{
			var log = new DisposalLog();
			var instance = new DisposableStore(log);
			var services = new ServiceCollection();
			var coupon = services.AddGeunedaCoupon();
			coupon.Services.Replace(ServiceDescriptor.Singleton<ICouponStore>(instance));
			ProbedCouponStore.Decorate(coupon, new RedeemProbe());

			await using (var provider = services.BuildServiceProvider())
			{
				Assert.IsType<ProbedCouponStore>(provider.GetRequiredService<ICouponStore>());
			}

			Assert.Equal(0, log.Disposed);
		}

		private sealed class DisposalLog
		{
			public int Created;
			public int Disposed;
		}

		/// <summary>해제를 기록하는 저장소입니다(나머지는 메모리 저장소에 맡김).</summary>
		private sealed class DisposableStore : ICouponStore, IAsyncDisposable
		{
			private readonly InMemoryCouponStore _inner = new InMemoryCouponStore();
			private readonly DisposalLog _log;

			public DisposableStore(DisposalLog log)
			{
				_log = log;
				_log.Created++;
			}

			public ValueTask DisposeAsync()
			{
				_log.Disposed++;
				return default;
			}

			public Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.AddCampaignAsync(campaign, cancellationToken);

			public Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.UpdateCampaignAsync(campaign, cancellationToken);

			public Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken) => _inner.GetCampaignAsync(campaignId, cancellationToken);

			public Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken) =>
				_inner.AddCodesAsync(campaignId, codes, cancellationToken);

			public Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken) => _inner.FindCodeAsync(code, cancellationToken);

			public Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken) =>
				_inner.FindRedemptionAsync(campaignId, accountId, cancellationToken);

			public Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
				CancellationToken cancellationToken) => _inner.RedeemAsync(attempt, grant, cancellationToken);
		}
	}
}
