using System;

namespace Geuneda.Coupon.Server
{
	/// <summary>지급자에 넘기는 지급 요청입니다.</summary>
	public sealed class CouponGrant
	{
		public CouponGrant(CouponCampaign campaign, string code, CouponContext context, DateTimeOffset redeemedAt)
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
