using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 캠페인·개별 코드·사용 기록 저장소입니다. 사용 기록과 게임 지급을 한 트랜잭션으로 묶어 같은 캠페인을 계정당 한 번, 개별 코드를 한 번만 지급합니다.
	/// 게임 DB 로 구현합니다(개발·테스트용 <see cref="InMemoryCouponStore"/>). 값 검사와 코드 다듬기는 <see cref="CouponAdmin"/> 이 하고 저장소에는 다듬은 값이 옵니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 구현 방법(관계형 DB 예): 캠페인 표(ID 유일, 공용 코드 유일), 개별 코드 표(코드 유일, 캠페인 ID, 쓴 계정), 사용 기록 표((캠페인 ID, 계정 ID) 유일, 코드, 시각, 지급 결과 JSON).
	/// 공용 코드와 개별 코드는 한 이름 공간이라 서로 겹치면 안 됩니다(코드 하나로 캠페인 하나를 찾음). 지급자가 같은 트랜잭션을 쓰게 하려면 요청 범위(scoped) DB 컨텍스트를 함께 받게 합니다.
	/// </para>
	/// <para>
	/// 동시성(<see cref="RedeemAsync"/>): 경쟁마다 막는 수단이 다릅니다. 유일 제약만으로는 서로 다른 계정의 경쟁을 막지 못합니다(두 계정의 기록은 키가 달라 둘 다 들어감).
	/// <list type="bullet">
	/// <item>같은 계정의 동시 요청: 사용 기록의 (캠페인 ID, 계정 ID) 유일 제약. 커밋 때 걸리면 게임 지급까지 전부 롤백하고 처음부터 다시 판정합니다(<see cref="CouponStoreOutcome.Duplicate"/>).</item>
	/// <item>전체 한도(<see cref="CouponCampaign.MaxRedemptions"/>): 캠페인 행을 트랜잭션 안에서 잠그고(<c>SELECT ... FOR UPDATE</c>) 사용 수를 다시 읽거나,
	/// <c>UPDATE ... SET used = used + 1 WHERE id = @id AND used &lt; @max</c> 같은 조건부 원자 증가를 하고 바뀐 행이 없으면 <see cref="CouponStoreOutcome.SoldOut"/>.</item>
	/// <item>개별 코드: 코드 행을 잠그거나 <c>UPDATE ... SET used_by = @account WHERE code = @code AND used_by IS NULL</c> 같은 조건부 갱신을 하고 바뀐 행이 없으면 <see cref="CouponStoreOutcome.CodeUsed"/>.</item>
	/// </list>
	/// 조건부 갱신을 지급 뒤에 하면, 실패했을 때 지급까지 전부 롤백해야 합니다(지급 전에 하면 지급 실패 때 함께 롤백). 트랜잭션 밖에서 읽은 값(<see cref="FindCodeAsync"/>,
	/// <see cref="FindRedemptionAsync"/>)으로 판정하지 마세요.
	/// </para>
	/// </remarks>
	public interface ICouponStore
	{
		/// <summary>캠페인을 넣습니다. ID 나 공용 코드가 이미 있으면 넣지 않고 그 결과입니다.</summary>
		Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken);

		/// <summary>같은 ID 의 캠페인을 바꿉니다(종류·공용 코드는 <see cref="CouponAdmin"/> 이 같은지 확인함). 없으면 false 입니다.</summary>
		Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken);

		/// <summary>캠페인을 찾습니다. 없으면 null 입니다.</summary>
		Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken);

		/// <summary>개별 코드 캠페인에 코드(비교용 모양, 서로 겹치지 않음)를 넣습니다. 이미 어딘가 있는 코드는 넣지 않고 결과에 알립니다.</summary>
		Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken);

		/// <summary>코드(비교용 모양)로 캠페인을 찾습니다. 공용 코드와 개별 코드를 모두 봅니다. 없으면 null 입니다.</summary>
		Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken);

		/// <summary>이 계정의 이 캠페인 사용 기록입니다. 없으면 null 입니다. 트랜잭션 밖의 먼저 확인용입니다(끝난 캠페인의 재요청에도 처음 결과를 돌려주려고).</summary>
		Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken);

		/// <summary>
		/// 한 트랜잭션에서 순서대로 판정합니다: 이 계정의 이 캠페인 기록이 있으면 <see cref="CouponStoreOutcome.Duplicate"/>(있던 기록), 개별 코드를 다른 계정이 썼으면
		/// <see cref="CouponStoreOutcome.CodeUsed"/>, 캠페인 사용 수가 <see cref="CouponCampaign.MaxRedemptions"/> 에 닿았으면 <see cref="CouponStoreOutcome.SoldOut"/>.
		/// 모두 통과하면 <paramref name="grant"/> 를 같은 트랜잭션에서 부르고, 지급했으면 기록(지급 결과 JSON 포함)·개별 코드 사용 표시·사용 수를 함께 커밋해
		/// <see cref="CouponStoreOutcome.Recorded"/>, 지급하지 않았으면 롤백해 <see cref="CouponStoreOutcome.NotGranted"/> 입니다.
		/// </summary>
		/// <remarks>
		/// <paramref name="grant"/> 가 예외를 던지면 롤백하고 예외를 그대로 던집니다. 동시 요청과 경쟁해 커밋이 실패하면 <paramref name="grant"/> 를 이미 불렀더라도 전부 롤백하고 처음부터 다시 합니다
		/// (그래서 <paramref name="grant"/> 는 한 요청에서 여러 번 불릴 수 있고, 게임 쓰기는 모두 이 트랜잭션 안이어야 합니다).
		/// </remarks>
		Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
			CancellationToken cancellationToken);
	}
}
