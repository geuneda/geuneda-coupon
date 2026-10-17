using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 계정별 코드 실패 기록입니다(무작위 대입 방지). 기본은 <see cref="InMemoryCouponAttemptStore"/> 이고, 서버가 여러 대면 공유 저장소(DB, Redis)로 구현해
	/// <see cref="CouponServerBuilder.AddAttemptStore{T}"/> 로 붙입니다(서버마다 따로 세면 대수만큼 더 시도할 수 있음).
	/// </summary>
	/// <remarks>
	/// <para>
	/// 사용 요청은 판정 전에 실패 하나를 미리 기록(예약)하고, 판정이 코드 실패가 아니면 그 예약을 지웁니다(<see cref="ReleaseAsync"/>). 확인과 예약이 한 번에 일어나므로
	/// 동시에 보낸 요청도 창 안 실패·진행 중 요청을 합쳐 한도만큼만 판정에 들어갑니다(확인과 기록을 따로 하면 동시 요청 수만큼 한도를 넘음, 0.1.0 리뷰 1회차).
	/// 서버가 판정 중에 죽으면 예약이 실패로 남습니다(보수적). 진행 중 요청도 한도에 들어가므로 한 계정이 한도보다 많은 요청을 동시에 보내면 실패가 없어도 일부는 잠금(429)입니다.
	/// </para>
	/// <para>
	/// 구현 방법: <see cref="TryReserveAsync"/> 는 계정마다 배타적으로(DB 면 계정 행 잠금 <c>SELECT ... FOR UPDATE</c> 또는 권고 잠금, Redis 면 Lua 스크립트)
	/// 창 안 기록 수를 세고 한도 미만일 때만 넣습니다. 세기와 넣기 사이에 다른 요청이 끼어들면 안 됩니다. 예약은 사용 처리 트랜잭션과 따로 바로 커밋합니다(다른 요청이 봐야 함).
	/// Postgres(권고 잠금)·Redis(Lua 와 정렬 집합) 예시는 패키지의 <c>docs/db-store.md</c> 입니다.
	/// </para>
	/// </remarks>
	public interface ICouponAttemptStore
	{
		/// <summary>
		/// 이 계정의 <paramref name="since"/> 이후(포함) 기록 수가 <paramref name="limit"/> 미만이면 <paramref name="at"/> 시각의 기록 하나를 넣고 그 예약을, 아니면 거절과 창 안 기록 시각들을 돌려줍니다.
		/// <paramref name="since"/> 보다 이른 기록은 지워도 됩니다(더 이상 세지 않음).
		/// </summary>
		Task<CouponAttemptReservation> TryReserveAsync(string accountId, DateTimeOffset at, DateTimeOffset since, int limit, CancellationToken cancellationToken);

		/// <summary>예약을 지웁니다(판정이 코드 실패가 아니었음). 이미 지워졌으면 아무것도 하지 않습니다.</summary>
		Task ReleaseAsync(string accountId, string reservationId, CancellationToken cancellationToken);
	}
}
