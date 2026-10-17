using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 사용 응답입니다. <typeparamref name="TGrant"/> 는 게임 서버의 지급 결과(보상 목록 등)입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// 서버는 지급 결과를 사용 기록에 함께 저장해, 같은 계정이 같은 캠페인을 다시 보내면 <see cref="CouponResultCodes.AlreadyRedeemed"/> 와 처음 결과를 돌려줍니다.
	/// </remarks>
	[Preserve]
	public sealed class CouponRedeemResponse<TGrant>
	{
		/// <summary>
		/// 0 이면 이번에 지급함. 그 밖은 <see cref="CouponResultCodes"/> 의 값. 서버는 늘 넣습니다. 클라이언트는 없으면(null) 쿠폰 서버의 응답이 아니라고 봅니다
		/// (int 기본값 0 으로 읽어 지급하지 않은 요청을 성공으로 알리지 않게)
		/// </summary>
		[Preserve]
		public int? Code { get; set; }

		/// <summary>개발자용 설명. 사용자에게 그대로 보여 주지 않습니다. 없으면 null</summary>
		[Preserve]
		public string Message { get; set; }

		/// <summary>코드가 속한 캠페인. 지급했거나 이미 지급했을 때만 있습니다</summary>
		[Preserve]
		public string CampaignId { get; set; }

		/// <summary>게임 서버의 지급 결과. 지급했거나 이미 지급했을 때만 있습니다</summary>
		[Preserve]
		public TGrant Grant { get; set; }

		/// <summary>지급한 시각(UTC). 지급했거나 이미 지급했을 때만 있습니다</summary>
		[Preserve]
		public DateTimeOffset? RedeemedAt { get; set; }

		/// <summary><see cref="CouponResultCodes.TooManyAttempts"/> 일 때 다시 시도할 수 있을 때까지 남은 초. 그 밖은 null</summary>
		[Preserve]
		public int? RetryAfterSeconds { get; set; }
	}
}
