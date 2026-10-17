using System;

namespace Geuneda.Coupon.Server
{
	/// <summary>쿠폰 서버 설정입니다.</summary>
	public sealed class CouponServerOptions
	{
		/// <summary>
		/// 계정이 <see cref="FailureWindow"/> 안에 낼 수 있는 코드 실패 수입니다(<see cref="CouponResultCodes.IsCodeFailure"/>). 닿으면 가장 오래된 실패가 창 밖으로 나갈 때까지
		/// <see cref="CouponResultCodes.TooManyAttempts"/>(HTTP 429) 입니다. 기본 10. 1 이상
		/// </summary>
		public int MaxFailures { get; set; } = 10;

		/// <summary>실패를 세는 창입니다. 기본 1시간. 0 보다 커야 합니다</summary>
		public TimeSpan FailureWindow { get; set; } = TimeSpan.FromHours(1);

		/// <summary>요청 본문 최대 바이트입니다. 넘으면 413. 기본 4096. 256 이상</summary>
		public int MaxRequestBytes { get; set; } = 4096;
	}
}
