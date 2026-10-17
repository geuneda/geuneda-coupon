using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 쿠폰 사용 처리입니다(요청 범위). HTTP 엔드포인트(<c>MapGeunedaCoupon</c>)가 쓰고, 게임이 다른 통로(실시간 RPC 등)로 받을 때 직접 부를 수 있습니다.
	/// </summary>
	public interface ICouponRedeemer
	{
		/// <summary>
		/// 순서: 잠금 확인 → 코드 다듬기·찾기 → 이 계정의 이전 사용(있으면 처음 결과) → 켜짐·기간·조건 → 저장소 트랜잭션(중복, 개별 코드 사용, 한도, 지급).
		/// 판정 전에 계정별 실패 하나를 원자적으로 예약하고(<see cref="ICouponAttemptStore.TryReserveAsync"/>, 한도면 잠금), 판정이 코드 실패(<see cref="CouponResultCodes.IsCodeFailure"/>)가
		/// 아니면 예약을 지웁니다. 그래서 동시 요청도 한도만큼만 판정에 들어갑니다.
		/// </summary>
		/// <param name="code">사용자가 입력한 코드(다듬기 전)</param>
		/// <param name="context">인증한 계정과 대상 조건 맥락</param>
		/// <remarks>지급자·저장소 예외는 잡지 않습니다. 지급자가 정의되지 않은 결과(<c>default</c>)를 주면 <see cref="System.InvalidOperationException"/> 입니다.</remarks>
		Task<CouponRedeemOutcome> RedeemAsync(string code, CouponContext context, CancellationToken cancellationToken);
	}
}
