using System;

namespace Geuneda.Coupon.Server
{
	/// <summary>저장소에 넘기는 사용 시도입니다. 활성·기간·조건은 이미 통과했습니다.</summary>
	public sealed class CouponRedeemAttempt
	{
		public CouponRedeemAttempt(CouponCampaign campaign, string code, CouponContext context, DateTimeOffset redeemedAt)
		{
			Campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
			Code = code ?? throw new ArgumentNullException(nameof(code));
			Context = context ?? throw new ArgumentNullException(nameof(context));
			RedeemedAt = redeemedAt;
		}

		public CouponCampaign Campaign { get; }

		/// <summary>쓴 코드(비교용 모양)입니다.</summary>
		public string Code { get; }

		public CouponContext Context { get; }

		public string AccountId => Context.AccountId;

		public DateTimeOffset RedeemedAt { get; }
	}
}
