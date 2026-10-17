namespace Geuneda.Coupon.Server
{
	/// <summary>캠페인 종류입니다. <c>default</c>(0)는 정의되지 않은 값입니다.</summary>
	public enum CouponKind
	{
		/// <summary>모두가 같은 코드 하나를 입력합니다(예: LUCKY2026). 계정당 한 번</summary>
		Shared = 1,

		/// <summary>코드를 여러 개 등록하고 각 코드는 한 번만 씁니다. 같은 캠페인의 코드는 계정당 하나만</summary>
		Unique = 2,
	}
}
