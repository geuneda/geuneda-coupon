using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Geuneda.Coupon.Server.Tests
{
	/// <summary>
	/// 등록된 저장소를 감싸 사용 처리(<see cref="ICouponStore.RedeemAsync"/>)에 들어온 요청을 캠페인·계정별로 셉니다. 동시성 시험이 뒤 요청이 이전 사용 확인을 지나
	/// 저장소 트랜잭션까지 왔는지 확인한 뒤 지급을 풀게 합니다(먼저 처리된 요청의 기록을 보고 끝나 저장소 경쟁을 건너뛰지 않게).
	/// </summary>
	internal sealed class ProbedCouponStore : ICouponStore
	{
		private const string InnerKey = "Geuneda.Coupon.Server.Tests.ProbedInner";

		private readonly ICouponStore _inner;
		private readonly RedeemProbe _probe;

		public ProbedCouponStore(ICouponStore inner, RedeemProbe probe)
		{
			_inner = inner;
			_probe = probe;
		}

		/// <summary>
		/// 지금 등록된 저장소(인스턴스, 팩토리, 형식 어느 것이든)를 같은 수명으로 감쌉니다. 원래 등록은 키 서비스로 옮겨 컨테이너가 계속 만들고 해제합니다
		/// (형식·팩토리로 만든 저장소는 범위가 끝나면 해제, 밖에서 넘긴 인스턴스는 해제하지 않음).
		/// </summary>
		public static void Decorate(CouponServerBuilder coupon, RedeemProbe probe)
		{
			var inner = coupon.Services.Last(descriptor => descriptor.ServiceType == typeof(ICouponStore) && !descriptor.IsKeyedService);
			coupon.Services.Remove(inner);
			coupon.Services.Add(ToKeyed(inner));
			coupon.Services.Add(ServiceDescriptor.Describe(typeof(ICouponStore),
				provider => new ProbedCouponStore(provider.GetRequiredKeyedService<ICouponStore>(InnerKey), probe), inner.Lifetime));
		}

		private static ServiceDescriptor ToKeyed(ServiceDescriptor descriptor)
		{
			if (descriptor.ImplementationInstance != null)
			{
				return new ServiceDescriptor(typeof(ICouponStore), InnerKey, descriptor.ImplementationInstance);
			}

			var factory = descriptor.ImplementationFactory;
			return factory != null
				? new ServiceDescriptor(typeof(ICouponStore), InnerKey, (provider, _) => factory(provider), descriptor.Lifetime)
				: new ServiceDescriptor(typeof(ICouponStore), InnerKey, descriptor.ImplementationType, descriptor.Lifetime);
		}

		public Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
			CancellationToken cancellationToken)
		{
			_probe.Enter(attempt.Campaign.Id, attempt.AccountId);
			return _inner.RedeemAsync(attempt, grant, cancellationToken);
		}

		public Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.AddCampaignAsync(campaign, cancellationToken);

		public Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.UpdateCampaignAsync(campaign, cancellationToken);

		public Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken) => _inner.GetCampaignAsync(campaignId, cancellationToken);

		public Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken) =>
			_inner.AddCodesAsync(campaignId, codes, cancellationToken);

		public Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken) => _inner.FindCodeAsync(code, cancellationToken);

		public Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken) =>
			_inner.FindRedemptionAsync(campaignId, accountId, cancellationToken);
	}

	/// <summary>사용 처리에 들어온 요청 수(캠페인, 계정)입니다.</summary>
	internal sealed class RedeemProbe
	{
		private readonly ConcurrentDictionary<(string CampaignId, string AccountId), int> _entered =
			new ConcurrentDictionary<(string CampaignId, string AccountId), int>();

		public void Enter(string campaignId, string accountId)
		{
			_entered.AddOrUpdate((campaignId, accountId), 1, (_, count) => count + 1);
		}

		public int Entered(string campaignId, string accountId)
		{
			return _entered.TryGetValue((campaignId, accountId), out var count) ? count : 0;
		}
	}
}
