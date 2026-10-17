# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.1] - 2026-10-17

**Changed**:
- 실제 DB 저장소 안내 `docs/db-store.md`: Postgres 표(사용 처리가 갱신하는 캠페인 행을 외래 키로 참조하지 않음), 지급자가 같은 트랜잭션에 참여하는 요청 범위 세션, 사용 기록 자리를 먼저 차지하는 `ICouponStore` 전체 구현, 기록을 맨 뒤에 넣는 순서의 0행 재확인, 사용 수를 지급 전·뒤에 늘리는 맞바꿈, 공용·개별 코드 이름 공간, 실패 기록 Postgres(권고 잠금)·Redis(Lua 와 정렬 집합) 구현, 운영 주의(Postgres 18 multixact 오류, 진행 중 요청과 실패 한도, 트랜잭션 밖 켜짐·기간 판정). 문서의 SQL·C# 를 그대로 뽑아 Postgres 18.3·Redis 8.10 동시 요청 시험으로 확인
- `ICouponStore`·`ICouponGrantor`·`ICouponAttemptStore` 문서와 README: 권장 순서(기록 자리 먼저)와, 조건부 갱신 0행을 바로 `CodeUsed`·`SoldOut` 으로 끝내면 같은 계정의 동시 요청이 처음 결과 대신 코드 실패를 받고 실패 제한에도 세는 경쟁, 저장한 지급 시각 돌려주기, 지급자의 트랜잭션 밖 부작용
- 테스트 `ConcurrentRequests_GrantOnce` 가 지급자 호출 수 대신 결과 코드, 처음 결과(지급 JSON·시각), 확정된 지급(`CouponTestServer.CommittedGrants`), 기록 수를 보고, 마지막 한도와 같은 개별 코드에서 같은 계정의 동시 요청이 `AlreadyRedeemed` 인지 확인. 저장소 데코레이터(`ProbedCouponStore`)로 모든 요청이 저장소 사용 처리에 들어온 뒤 지급을 풀어 경쟁을 반드시 거침(실제 DB 저장소에도 그대로 쓰는 계약 시험)
- 테스트 서버·샘플 서버·README 의 범위를 `await using ... CreateAsyncScope()` 로(동기 해제는 `IAsyncDisposable` 만 구현한 요청 범위 DB 세션에서 예외)

## [0.1.0] - 2026-10-17

**New**:
- `ICouponService`/`CouponService`: 코드를 다듬어(공백·`-` 제거, 대문자) `POST {기본 주소}redeem` 으로 보내고 지급 결과를 게임 형식(`CouponRedemption<TGrant>`)으로 돌려줌. 이미 쓴 캠페인은 처음 지급 결과와 함께 `AlreadyRedeemed` 성공. 성공은 HTTP 200 + 결과 코드 + 비어 있지 않은 캠페인 ID 가 있을 때만, 본문 없는 401·429·4xx 는 상태로 코드, 그 밖은 `HttpError`·`InvalidResponse`
- 공유 형식(`Runtime/Protocol`, 서버가 링크): `CouponRedeemRequest`/`CouponRedeemResponse<TGrant>`, `CouponResultCodes`(11000 블록, `IsCodeFailure`), `CouponCodes.TryNormalize`, `CouponPlatforms`, `CouponRoutes`
- `Server~`: ASP.NET Core 라이브러리 `Geuneda.Coupon.Server`. `AddGeunedaCoupon`/`MapGeunedaCoupon`, 공용 코드(계정당 한 번, 전체 한도)·개별 일회용 코드(코드당 한 번, 캠페인당 계정 하나) 캠페인(`CouponCampaign`, `CouponAdmin`), 켜짐·기간, 대상 조건(`CouponConditions`: 플랫폼·국가·최소 앱 버전, `ICouponContextResolver` 로 서버 값), 계정별 코드 실패 제한(`ICouponAttemptStore`, 기본 1시간 10번, 판정 전 원자 예약이라 동시 요청도 한도 안, 429 + Retry-After), 사용 기록과 게임 지급(`ICouponGrantor`)을 저장소(`ICouponStore`, 메모리 구현) 한 트랜잭션으로. 샘플 서버와 Unity 상호 운용 테스트
