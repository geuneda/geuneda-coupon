using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 사용입니다. 기본 구현은 <see cref="CouponService"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다. 보상은 서버가 지급합니다.
	/// </summary>
	public interface ICouponService
	{
		/// <summary>
		/// 코드를 서버(<c>Geuneda.Coupon.Server</c> 의 <c>MapGeunedaCoupon</c>)에 보내 씁니다. 코드는 공백·<c>-</c> 를 빼고 대문자로 바꿔 보내고,
		/// 코드 모양이 아니면 보내지 않고 <see cref="CouponResultCodes.InvalidCode"/> 입니다.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 성공: 이번에 지급했거나, 이 계정이 이미 쓴 캠페인(<see cref="CouponRedemption{TGrant}.AlreadyRedeemed"/>, 처음 지급 결과)입니다.
		/// 실패: <see cref="CouponResultCodes"/>(없는 코드, 기간, 사용됨, 한도, 조건, 지급 거절·보류, 실패가 많아 잠김, 인증), 연결 실패·제한 시간·취소는 core 공용 코드,
		/// 해석할 수 없는 응답(5xx 등)은 <c>HttpResultCodes.HttpError</c>·<c>InvalidResponse</c> 입니다.
		/// </para>
		/// <para>
		/// 제한 시간·취소·연결 실패면 서버가 지급했는지 모릅니다. 같은 코드를 다시 보내면 지급했을 때 <c>AlreadyRedeemed</c> 와 처음 결과가 옵니다.
		/// </para>
		/// </remarks>
		/// <typeparam name="TGrant">게임 서버 지급 결과의 형식(서버 <c>CouponGrantResult.Granted(response)</c> 의 JSON)</typeparam>
		UniTask<Result<CouponRedemption<TGrant>>> RedeemAsync<TGrant>(string code, CancellationToken cancellationToken = default);
	}
}
