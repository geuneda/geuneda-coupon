// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 사용 요청입니다. 계정은 본문이 아니라 인증(Bearer 토큰)으로 정합니다. 플랫폼·앱 버전·국가는 대상 조건 판정에 쓰는 클라이언트 값이라 위조할 수 있습니다
	/// (서버가 <c>ICouponContextResolver</c> 로 바꿔 끼울 수 있음). 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	[Preserve]
	public sealed class CouponRedeemRequest
	{
		/// <summary>사용자가 입력한 코드. 서버가 공백·<c>-</c> 를 빼고 대문자로 바꿉니다(<see cref="CouponCodes.TryNormalize"/>)</summary>
		[Preserve]
		public string Code { get; set; }

		/// <summary><see cref="CouponPlatforms"/> 의 이름. 없으면 null</summary>
		[Preserve]
		public string Platform { get; set; }

		/// <summary>앱 버전(<c>Application.version</c>). 없으면 null</summary>
		[Preserve]
		public string AppVersion { get; set; }

		/// <summary>ISO 3166-1 두 글자 국가 코드. 없으면 null</summary>
		[Preserve]
		public string Country { get; set; }
	}
}
