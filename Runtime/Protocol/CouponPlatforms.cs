// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 대상 조건의 플랫폼 이름입니다. 게임이 다른 이름(스토어별 등)을 써도 됩니다(1~32자 영문 소문자·숫자·<c>_</c>·<c>-</c>).
	/// 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public static partial class CouponPlatforms
	{
		public const string Android = "android";
		public const string Ios = "ios";
		public const string Standalone = "standalone";
		public const string WebGl = "webgl";
	}
}
