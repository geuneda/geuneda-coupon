// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 서버 경로입니다. 서버 접두사(예: <c>/coupons</c>) 아래의 상대 경로이고, 클라이언트는 접두사까지 담은 기본 주소(예: <c>https://example.com/coupons/</c>)에 붙입니다.
	/// 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public static class CouponRoutes
	{
		/// <summary>POST <see cref="CouponRedeemRequest"/> → <see cref="CouponRedeemResponse{TGrant}"/>. 계정 인증 필요</summary>
		public const string Redeem = "redeem";
	}
}
