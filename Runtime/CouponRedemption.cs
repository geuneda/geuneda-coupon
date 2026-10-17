using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>쿠폰 사용 성공입니다. <see cref="AlreadyRedeemed"/> 면 이번 요청이 아니라 처음 사용 때 지급한 결과입니다.</summary>
	/// <typeparam name="TGrant">게임 서버 지급 결과(보상 목록 등)의 형식</typeparam>
	public sealed class CouponRedemption<TGrant>
	{
		public CouponRedemption(string campaignId, TGrant grant, DateTimeOffset? redeemedAt, bool alreadyRedeemed)
		{
			CampaignId = campaignId;
			Grant = grant;
			RedeemedAt = redeemedAt;
			AlreadyRedeemed = alreadyRedeemed;
		}

		public string CampaignId { get; }

		/// <summary>게임 서버의 지급 결과입니다. 서버가 주지 않았으면 <c>default</c> 입니다.</summary>
		public TGrant Grant { get; }

		/// <summary>지급한 시각(UTC)입니다.</summary>
		public DateTimeOffset? RedeemedAt { get; }

		/// <summary>
		/// 이 계정이 이 캠페인을 이미 썼는지입니다(<see cref="CouponResultCodes.AlreadyRedeemed"/>). 지급은 처음 사용 때 끝났으므로 보상을 다시 더하지 않습니다.
		/// 응답을 잃고 다시 보낸 경우에도 true 이니, 안내는 '이미 사용한 쿠폰'으로 하고 보상은 서버 상태로 맞춥니다.
		/// </summary>
		public bool AlreadyRedeemed { get; }
	}
}
