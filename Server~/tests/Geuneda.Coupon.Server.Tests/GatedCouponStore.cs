using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server.Tests
{
	/// <summary>메모리 저장소를 감싸 코드 찾기를 <see cref="Gate"/> 가 열릴 때까지 붙잡습니다. 판정에 들어간 요청 수를 셉니다.</summary>
	internal sealed class GatedCouponStore : ICouponStore
	{
		private readonly InMemoryCouponStore _inner;
		private int _lookups;

		public GatedCouponStore(InMemoryCouponStore inner)
		{
			_inner = inner;
		}

		public TaskCompletionSource Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		public int Lookups => Volatile.Read(ref _lookups);

		public async Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _lookups);
			await Gate.Task;
			return await _inner.FindCodeAsync(code, cancellationToken);
		}

		public Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.AddCampaignAsync(campaign, cancellationToken);

		public Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken) => _inner.UpdateCampaignAsync(campaign, cancellationToken);

		public Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken) => _inner.GetCampaignAsync(campaignId, cancellationToken);

		public Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken) =>
			_inner.AddCodesAsync(campaignId, codes, cancellationToken);

		public Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken) =>
			_inner.FindRedemptionAsync(campaignId, accountId, cancellationToken);

		public Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
			CancellationToken cancellationToken) => _inner.RedeemAsync(attempt, grant, cancellationToken);
	}
}
