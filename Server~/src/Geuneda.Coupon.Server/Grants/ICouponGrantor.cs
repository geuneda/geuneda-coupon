using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 게임이 구현하는 지급자입니다. 캠페인 보상(<see cref="CouponCampaign.Reward"/>)을 계정에 넣습니다.
	/// 저장소(<see cref="ICouponStore"/>)의 트랜잭션 안에서 불리므로, 게임 데이터 쓰기는 그 트랜잭션에 참여해야 합니다(같은 DB 연결·세션).
	/// </summary>
	/// <remarks>
	/// <para>같은 계정·캠페인으로는 한 번만 성공합니다. 저장소가 이미 기록한 사용이면 부르지 않습니다. 동시 요청 경쟁으로 롤백되면 한 요청에서 다시 불릴 수 있습니다.</para>
	/// <para>
	/// 계정 조건(레벨, 가입일 등)은 여기서 <see cref="CouponGrantResult.Rejected"/> 로 거절합니다. <see cref="CouponGrantResult.Rejected"/>·<see cref="CouponGrantResult.RetryLater"/> 를 돌려주거나
	/// 예외를 던지면 기록하지 않습니다(트랜잭션 롤백). 예외는 잡지 않아 HTTP 500 이 됩니다.
	/// </para>
	/// </remarks>
	public interface ICouponGrantor
	{
		Task<CouponGrantResult> GrantAsync(CouponGrant grant, CancellationToken cancellationToken);
	}
}
