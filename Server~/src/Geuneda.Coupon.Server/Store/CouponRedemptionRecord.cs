using System;

namespace Geuneda.Coupon.Server
{
	/// <summary>사용 기록 하나입니다. 캠페인·계정마다 하나(유일 키)이고, 개별 코드 캠페인이면 코드도 유일합니다.</summary>
	public sealed class CouponRedemptionRecord
	{
		/// <param name="grantJson">지급자 결과(<see cref="CouponGrantResult.Response"/>)의 JSON. 없으면 null. 같은 계정이 다시 보내면 이것을 돌려줍니다</param>
		public CouponRedemptionRecord(string campaignId, string accountId, string code, DateTimeOffset redeemedAt, string grantJson)
		{
			CampaignId = campaignId ?? throw new ArgumentNullException(nameof(campaignId));
			AccountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
			Code = code ?? throw new ArgumentNullException(nameof(code));
			RedeemedAt = redeemedAt;
			GrantJson = grantJson;
		}

		public string CampaignId { get; }

		public string AccountId { get; }

		/// <summary>쓴 코드(비교용 모양)입니다.</summary>
		public string Code { get; }

		public DateTimeOffset RedeemedAt { get; }

		public string GrantJson { get; }
	}
}
