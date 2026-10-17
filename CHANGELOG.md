# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-17

**New**:
- `ICouponService`/`CouponService`: 코드를 다듬어(공백·`-` 제거, 대문자) `POST {기본 주소}redeem` 으로 보내고 지급 결과를 게임 형식(`CouponRedemption<TGrant>`)으로 돌려줌. 이미 쓴 캠페인은 처음 지급 결과와 함께 `AlreadyRedeemed` 성공. 성공은 HTTP 200 + 결과 코드 + 비어 있지 않은 캠페인 ID 가 있을 때만, 본문 없는 401·429·4xx 는 상태로 코드, 그 밖은 `HttpError`·`InvalidResponse`
- 공유 형식(`Runtime/Protocol`, 서버가 링크): `CouponRedeemRequest`/`CouponRedeemResponse<TGrant>`, `CouponResultCodes`(11000 블록, `IsCodeFailure`), `CouponCodes.TryNormalize`, `CouponPlatforms`, `CouponRoutes`
- `Server~`: ASP.NET Core 라이브러리 `Geuneda.Coupon.Server`. `AddGeunedaCoupon`/`MapGeunedaCoupon`, 공용 코드(계정당 한 번, 전체 한도)·개별 일회용 코드(코드당 한 번, 캠페인당 계정 하나) 캠페인(`CouponCampaign`, `CouponAdmin`), 켜짐·기간, 대상 조건(`CouponConditions`: 플랫폼·국가·최소 앱 버전, `ICouponContextResolver` 로 서버 값), 계정별 코드 실패 제한(`ICouponAttemptStore`, 기본 1시간 10번, 판정 전 원자 예약이라 동시 요청도 한도 안, 429 + Retry-After), 사용 기록과 게임 지급(`ICouponGrantor`)을 저장소(`ICouponStore`, 메모리 구현) 한 트랜잭션으로. 샘플 서버와 Unity 상호 운용 테스트
