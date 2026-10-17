# Geuneda.Coupon - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.coupon`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(ModuleLogger, Result), `com.geuneda.http`(POST), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`. 서버(`Server~`)는 .NET 10, ASP.NET Core 입니다
- **역할**: 클라이언트는 코드를 보내고 결과를 받기만 합니다. 판정·지급은 서버입니다(보상은 서버가 지급)
- **서버 공유**: `Server~` 의 라이브러리가 `Runtime/Protocol/*.cs`(요청·응답 DTO, 결과 코드, 코드 다듬기, 플랫폼 이름, 경로)를 그대로 링크합니다
- **레퍼런스**: Percent `Marketings.Coupon`(서버 지급, `code == "SUCCESS"` 만 봄, 제한 시간 없음)과 SpellDefense `CouponService`(서버가 성공에도 `success=false` 를 내려 지급 내역으로 판정). 분석은 lucky-defense 레포 `data/analysis/tech/percent/analytics-liveops.md` 4절

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/ICouponService.cs`, `CouponService.cs`, `CouponServiceOptions.cs`, `CouponRedemption.cs` | 클라이언트 계약과 구현. 응답 판정(성공은 200 + 캠페인 ID) |
| `Runtime/CouponPlatforms.Current.cs` | 빌드 대상 플랫폼 이름 |
| `Runtime/Protocol/` | 서버와 함께 컴파일하는 공유 형식. core·UniTask·UnityEngine 을 쓰지 않습니다 |
| `Server~/src/Geuneda.Coupon.Server/Campaigns/` | `CouponCampaign`, `CouponKind`, `CouponConditions`, `CouponContext`, `ICouponContextResolver`, internal `CouponNames`·`CouponVersion` |
| `Server~/src/Geuneda.Coupon.Server/Store/` | `ICouponStore`(트랜잭션 계약), 결과 형식, `InMemoryCouponStore` |
| `Server~/src/Geuneda.Coupon.Server/Attempts/` | `ICouponAttemptStore`, `InMemoryCouponAttemptStore` |
| `Server~/src/Geuneda.Coupon.Server/Grants/` | `ICouponGrantor`, `CouponGrant`, `CouponGrantResult` |
| `Server~/src/Geuneda.Coupon.Server/Redeem/` | `ICouponRedeemer`/`CouponRedeemer`(처리 순서), `CouponRedeemOutcome`, `CouponRedeemEndpoint`(HTTP) |
| `Server~/src/Geuneda.Coupon.Server/Admin/CouponAdmin.cs` | 운영 입구(값 검사, 코드 다듬기, 종류·공용 코드 변경 막기) |
| `Server~/tests/`, `Server~/samples/` | xUnit(실제 Kestrel), 샘플 서버(메모리 저장소, 샘플 지갑) |

## 3. 규칙

- **`Runtime/Protocol/*.cs` 는 Unity 와 .NET 이 함께 컴파일합니다.** C# 9 까지만 쓰고(`Server~/Directory.Build.props` 가 `LangVersion 9.0`, 서버 코드 전체도 C# 9), UnityEngine·UniTask·core 를 넣지 않습니다. JSON 으로만 만드는 타입에는 `[Preserve]`(패키지 안의 internal 특성)를 붙입니다.
- **코드 다듬기는 ASCII 만**(`CouponCodes.TryNormalize`): 런타임 문자 표에 기대지 않습니다(banword 에서 Mono·.NET 차이 확인). 전각·터키어 `ı` 등은 코드가 아닙니다.
- **처리 순서(불변식, `CouponRedeemer`)**: 잠금 → 다듬기·찾기 → 이 계정의 이전 사용(있으면 기간·켜짐과 상관없이 처음 결과) → 켜짐·기간·조건 → 저장소 트랜잭션(중복 → 개별 코드 사용 → 한도 → 지급). 이전 사용 확인을 기간 검사 뒤로 옮기면 응답을 잃은 계정이 끝난 캠페인에서 처음 결과를 못 받습니다.
- **실패 세기(예약)**: 판정 전에 `ICouponAttemptStore.TryReserveAsync` 로 실패 하나를 원자적으로 예약하고(창 안 기록이 한도면 잠금), 판정이 `CouponResultCodes.IsCodeFailure` 가 아니면(이미 쓴 캠페인·지급 거절·보류·예외·취소 포함) `ReleaseAsync` 로 지웁니다. 확인과 기록을 따로 하면 같은 실패 수를 읽은 동시 요청이 모두 판정에 들어갑니다(리뷰 1회차 🔴, `ConcurrentFailures_CannotPassTheLimitTogether` 가 지키고, 예약을 빼는 변형으로 실패를 확인함). 지우기는 `CancellationToken.None` 입니다. 잠금이 풀리는 시각은 창 경계의 기록을 아직 세므로 그 시각을 지난 첫 초입니다(`RetryAfterSeconds`). 잠금 경고 로그는 실제로 거절한 분기에서만 남깁니다(예약 때의 수는 진행 중 요청을 포함한 그때의 값이라 확정 실패 수가 아님, 리뷰 2회차 🟡, `InFlightSuccess_DoesNotLeaveAFalseLockWarning`).
- **지급자 결과**: `Granted` 만 기록합니다. `Rejected`·`RetryLater` 는 롤백하고 결과 코드로, `default`(정의되지 않은 값)는 `InvalidOperationException`(500). 지급자·저장소 예외는 잡지 않습니다(500, 롤백). 저장소가 경쟁으로 롤백한 뒤 지급 콜백을 다시 부를 수 있으므로 결과는 마지막 시도의 것을 씁니다.
- **지급 결과 저장**: 지급자 응답을 System.Text.Json(Web, null 제외)으로 직렬화해 기록에 넣고, 같은 계정의 재요청에 그 JSON 을 그대로 돌려줍니다.
- **저장소 동시성 문서**: 유일 제약은 같은 계정 중복만 막고, 전체 한도와 개별 코드는 행 잠금이나 조건부 갱신이 필요합니다(`ICouponStore` remarks, 리뷰 1회차 🟡). 메모리 저장소는 지급을 롤백하지 않습니다(기록 미커밋만 보장).
- **클라이언트 성공 판정**: 상태가 200·400·401·413·415·429 일 때만 본문을 결과로 읽고, `code` 가 없으면(DTO 의 `int? Code` 가 null) 쿠폰 응답이 아니며, 성공(0·`AlreadyRedeemed`)은 200 이고 캠페인 ID 가 비어 있지 않을 때만입니다(리뷰 1회차 🔴: `int` 이면 code 없는 JSON 이 0 이 됨). code 가 없는 JSON(ProblemDetails, 프록시 응답)이 0 으로 읽혀 지급하지 않은 요청을 성공으로 알리지 않게 하려는 것입니다. 본문을 못 읽은 401·429·400 계열은 상태로 코드를 정합니다(앞단 미들웨어).
- **맥락**: 클라이언트 값은 `CouponContext` 가 다듬고(플랫폼 소문자, 국가 대문자 두 글자, 앱 버전 64자 이하), 규칙에 맞지 않으면 null 입니다. 해석기가 계정 ID 를 바꾸면 500 입니다. 조건이 있는데 값이 null 이면 막힙니다.
- **등록 빌더**: `AddGrantor<T>`·`AddStore<T>` 는 `ServiceDescriptor.Describe(인터페이스, T)` 라 같은 T 를 따로 등록해도 별도 인스턴스입니다. 테스트에서 같은 인스턴스가 필요하면 팩토리로 넘깁니다(`CouponTestServer`).
- **정적 필드 순서**: `CouponConditions.None` 은 그 생성자가 쓰는 `Any` 뒤에 선언합니다(처음 구현에서 순서가 반대라 NRE, 테스트로 발견).
- **결과 코드**: 11000 블록(core README 표에 등록). 서버 응답과 클라이언트 `Result` 가 같은 값입니다. 코드를 더하면 `IsCodeFailure`, README 결과 표, 클라이언트 테스트를 함께 고칩니다.
- **static 가변 상태 금지**(D2). 로거만 static 입니다.

## 4. 테스트

- Unity: EditMode `Geuneda.Coupon.Tests.EditMode`(core·http·coupon·UniTask·Newtonsoft). `FakeCouponServer`(대본 응답), `SampleServerInteropTest`(명시 실행, `GEUNEDA_COUPON_INTEROP_URL`, 샘플 서버를 새로 띄운 뒤 한 번).
- .NET: `dotnet test "Server~/Geuneda.Coupon.Server.slnx"`. 127.0.0.1 빈 포트의 실제 Kestrel(`CouponTestServer`), `FakeTimeProvider`, 대본 지급자(`TestGrantor`, `Gate` 로 동시 요청).

## 5. 문서

- 공개 API, 와이어 형식, 처리 순서를 바꾸면 `README.md`, `CHANGELOG.md`, `package.json` 버전, geuneda-frameworks 호스트 레포의 `claude-skills/geuneda-coupon/`(SKILL.md, references/api.md)와 `claude-skills/geuneda-frameworks/` 를 함께 고치고 `claude-skills/install.sh geuneda-coupon`, `claude-skills/install.sh geuneda-frameworks` 로 설치합니다.
- `Runtime/Protocol` 을 바꾸면 서버와 클라이언트가 함께 바뀌므로 서버 테스트와 Unity 테스트, 상호 운용 테스트를 함께 돌립니다.
