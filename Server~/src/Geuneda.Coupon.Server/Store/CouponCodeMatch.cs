namespace Geuneda.Coupon.Server
{
	/// <summary>코드로 찾은 캠페인입니다.</summary>
	public sealed class CouponCodeMatch
	{
		public CouponCodeMatch(CouponCampaign campaign, string code)
		{
			Campaign = campaign ?? throw new System.ArgumentNullException(nameof(campaign));
			Code = code ?? throw new System.ArgumentNullException(nameof(code));
		}

		public CouponCampaign Campaign { get; }

		/// <summary>찾은 코드(비교용 모양)입니다. 공용 캠페인이면 <see cref="CouponCampaign.SharedCode"/>, 개별 코드 캠페인이면 그 개별 코드입니다.</summary>
		public string Code { get; }
	}
}
