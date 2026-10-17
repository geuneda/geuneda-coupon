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
	/// 구현 방법(관계형 DB 예): 캠페인 표(ID 유일, 공용 코드 유일, 사용 수), 개별 코드 표(코드 유일, 캠페인 ID, 쓴 계정), 사용 기록 표((캠페인 ID, 계정 ID) 유일, 코드, 시각,
	/// 지급 결과 JSON 은 받은 문자열 그대로). 공용 코드와 개별 코드는 한 이름 공간이라 서로 겹치면 안 됩니다(코드 하나로 캠페인 하나를 찾음). 두 표의 유일 제약은 표 사이의 겹침을
	/// 막지 못하므로 코드 하나를 키로 하는 표를 두거나 등록을 잠금으로 줄 세웁니다. 지급자가 같은 트랜잭션을 쓰게 하려면 저장소와 지급자가 요청 범위(scoped) DB 세션을 함께 받습니다.
	/// Postgres 예시(표, 세션, 이 순서의 전체 구현, 실제 DB 동시 요청 시험 결과)는 패키지의 <c>docs/db-store.md</c> 입니다.
	/// </para>
	/// <para>
	/// 동시성(<see cref="RedeemAsync"/>), 권장 순서: 유일 제약은 같은 계정의 중복만 막고, 서로 다른 계정의 경쟁(전체 한도, 개별 코드)은 조건부 갱신이나 행 잠금이 막습니다.
	/// <list type="number">
	/// <item>사용 기록 자리를 먼저 차지합니다(<c>INSERT ... ON CONFLICT (campaign_id, account_id) DO NOTHING</c>). 같은 계정의 동시 요청은 여기서 앞 트랜잭션이 끝날 때까지 기다리고,
	/// 앞이 커밋하면 넣지 못하므로 그 기록을 읽어 <see cref="CouponStoreOutcome.Duplicate"/> 입니다.</item>
	/// <item>개별 코드: <c>UPDATE ... SET used_by = @account WHERE code = @code AND used_by IS NULL</c>. 바뀐 행이 없으면 <see cref="CouponStoreOutcome.CodeUsed"/>.</item>
	/// <item>전체 한도(<see cref="CouponCampaign.MaxRedemptions"/>): <c>UPDATE ... SET used = used + 1 WHERE id = @id AND (max_redemptions IS NULL OR used &lt; max_redemptions)</c>.
	/// 바뀐 행이 없으면 <see cref="CouponStoreOutcome.SoldOut"/>.</item>
	/// <item>지급 콜백을 부르고, 지급했으면 지급 결과 JSON 을 기록에 써서 커밋합니다. 지급하지 않았거나 예외면 롤백합니다(자리, 코드, 사용 수, 게임 데이터가 함께 되돌아감).</item>
	/// </list>
	/// </para>
	/// <para>
	/// 사용 기록을 맨 뒤에 넣는 순서(기록 확인 → 조건부 갱신 → 지급 → 기록 INSERT)라면, 조건부 갱신이 바뀐 행 없이 끝났을 때 바로 CodeUsed·SoldOut 으로 끝내지 말고 같은 트랜잭션에서
	/// 이 계정의 기록을 다시 읽어 있으면 Duplicate 로 돌려줍니다(행 잠금 방식이면 잠근 뒤에 기록 확인). 같은 계정의 앞 요청이 그 코드나 마지막 한도를 방금 가져간 경우입니다.
	/// 그대로 끝내면 이미 받은 계정이 처음 결과 대신 코드 실패(<see cref="CouponResultCodes.IsCodeFailure"/>, 계정별 실패 제한에 셈)를 받습니다(실제 Postgres 에서 같은 계정 50개 동시 요청 중
	/// 4~17개). 이 순서는 같은 계정의 중복 요청마다 지급 콜백을 불렀다가 롤백하고, 기록 INSERT 가 유일 제약에 걸리면 전부 롤백하고 처음부터 다시 판정합니다.
	/// 트랜잭션 밖에서 읽은 값(<see cref="FindCodeAsync"/>, <see cref="FindRedemptionAsync"/>)으로 판정하지 마세요.
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
		/// 돌려주는 기록의 시각은 저장한 값입니다(DB 정밀도로 잘린 값. 넘겨받은 시각을 그대로 돌려주면 재요청의 기록과 달라짐).
		/// </summary>
		/// <remarks>
		/// <paramref name="grant"/> 가 예외를 던지면 롤백하고 예외를 그대로 던집니다. 같은 계정의 기록이 경쟁으로 먼저 들어가면 결과는 그 기록의 <see cref="CouponStoreOutcome.Duplicate"/> 입니다
		/// (조건부 갱신이 바뀐 행 없이 끝나도 같은 계정의 기록이 있으면 Duplicate). 경쟁으로 롤백한 뒤 다시 판정하는 구현에서는 <paramref name="grant"/> 가 한 요청에서 여러 번
		/// 불릴 수 있으므로 게임 쓰기는 모두 이 트랜잭션 안이어야 합니다.
		/// </remarks>
		Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
			CancellationToken cancellationToken);
	}
}
