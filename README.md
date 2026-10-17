# Geuneda Coupon

쿠폰 사용입니다. 클라이언트는 사용자가 입력한 코드를 서버에 보내고 지급 결과를 게임 형식으로 돌려받습니다. **보상은 서버가 지급합니다.** ASP.NET Core 서버 라이브러리(`Server~/`)가 공용 코드와 개별 일회용 코드, 기간, 대상 조건, 계정별 실패 제한을 판정하고, 사용 기록과 게임 지급을 저장소의 한 트랜잭션으로 묶습니다. 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 쿠폰 사용(Unity) | `ICouponService`, `CouponService`, `CouponServiceOptions`, `CouponRedemption<TGrant>` | `Geuneda.Coupon` |
| 공유 형식 | `CouponRedeemRequest`, `CouponRedeemResponse<TGrant>`, `CouponResultCodes`(11000 블록), `CouponCodes`, `CouponPlatforms`, `CouponRoutes` | `Geuneda.Coupon` |
| 서버(.NET, `Server~/`) | `AddGeunedaCoupon`, `MapGeunedaCoupon`, `CouponServerBuilder`, `CouponServerOptions`, `CouponCampaign`, `CouponConditions`, `CouponAdmin`, `ICouponStore`, `InMemoryCouponStore`, `ICouponGrantor`, `CouponGrantResult`, `ICouponAttemptStore`, `ICouponContextResolver`, `ICouponRedeemer` | `Geuneda.Coupon.Server` |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, http, UniTask 도 함께 넣습니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git",
    "com.geuneda.coupon": "https://github.com/geuneda/geuneda-coupon.git"
  }
}
```

## 사용법 (Unity)

```csharp
using Geuneda.Coupon;
using Geuneda.Http;

var coupons = new CouponService(new CouponServiceOptions
{
    // 서버 접두사까지 담은 기본 주소. 계정 인증은 account 패키지의 AuthTokenMiddleware 가 붙인다
    HttpClient = new HttpClientBuilder().WithBaseAddress("https://game.example.com/coupons/").AddMiddleware(authTokenMiddleware).Build(),
    // Platform 기본 CouponPlatforms.Current, AppVersion 기본 Application.version, Country 기본 없음, RequestTimeout 기본 10초
});
MainInstaller.Bind<ICouponService>(coupons);

var result = await coupons.RedeemAsync<MyGrant>(input.text, token);   // MyGrant: 게임 서버 지급 결과 형식

if (result.TryGetValue(out var redemption))
{
    if (redemption.AlreadyRedeemed)
    {
        ShowToast("이미 사용한 쿠폰입니다");   // 보상은 처음 사용 때 지급됨. 다시 더하지 않고 서버 상태로 맞춘다
    }
    else
    {
        ShowRewards(redemption.Grant);
    }

    await SyncWalletFromServerAsync();      // 보상은 서버가 넣었다
    return;
}

switch (result.Code)
{
    case CouponResultCodes.InvalidCode: ShowToast("없는 쿠폰입니다"); break;
    case CouponResultCodes.Expired: ShowToast("기간이 끝난 쿠폰입니다"); break;
    case CouponResultCodes.TooManyAttempts: ShowToast("잠시 후 다시 시도하세요"); break;
    case ResultCodes.Timeout: case ResultCodes.NetworkError: ShowRetry(); break;   // 서버가 지급했을 수 있음: 같은 코드를 다시 보내면 AlreadyRedeemed
    // ...
}
```

- 코드는 공백(스페이스, 탭)과 `-` 를 빼고 영문을 대문자로 바꿔 보냅니다(`welcome-2026` → `WELCOME2026`). 영문·숫자 4~64자가 아니면 보내지 않고 `InvalidCode` 입니다.
- **같은 코드를 다시 보내도 안전합니다.** 이 계정이 이미 쓴 캠페인이면 서버가 처음 지급 결과를 돌려주고 클라이언트는 `AlreadyRedeemed` 가 true 인 성공으로 받습니다. 제한 시간·연결 실패로 결과를 모르면 다시 보냅니다.
- 국가 조건이 있는 캠페인은 `Country` 를 넣거나(게임이 아는 값) 서버의 맥락 해석기로 정합니다. 기기 지역은 플랫폼마다 믿기 어려워 기본으로 보내지 않습니다.
- 메인 스레드에서 만듭니다(`Application.version`). 사용 버튼은 요청이 끝날 때까지 막습니다(두 번 눌러도 지급은 한 번이지만 두 번째는 `AlreadyRedeemed`).

### 결과

| 상황 | 결과 |
|------|------|
| 이번에 지급 | 성공, `AlreadyRedeemed` false, `Grant` |
| 이 계정이 이 캠페인을 이미 씀(다른 개별 코드로 포함) | 성공, `AlreadyRedeemed` true, 처음 `Grant` |
| 없는 코드·코드 형식 아님 | `CouponResultCodes.InvalidCode`(11001) |
| 시작 전, 끝남, 꺼짐 | `NotStarted`(11002), `Expired`(11003), `Disabled`(11004) |
| 개별 코드를 다른 계정이 씀 | `CodeUsed`(11006) |
| 캠페인 전체 한도 | `SoldOut`(11007) |
| 대상 조건(플랫폼·국가·최소 앱 버전) | `NotEligible`(11008) |
| 게임 서버가 거절·보류 | `GrantRejected`(11009), `GrantDeferred`(11010, 나중에 다시) |
| 코드 실패가 많아 잠김(HTTP 429) | `TooManyAttempts`(11011, 메시지에 남은 초) |
| 요청 형식 오류(400·413·415), 인증 실패(401) | `BadRequest`(11012), `Unauthorized`(11013) |
| 연결 실패, 제한 시간, 취소 | `ResultCodes.NetworkError`, `Timeout`, `Cancelled` |
| 쿠폰 서버의 응답이 아님(5xx, 프록시 오류, 결과 코드 없는 200) | `HttpResultCodes.HttpError`, `InvalidResponse` |

- 성공은 HTTP 200 이고 캠페인 ID 가 있는 응답만 받습니다(5xx 의 ProblemDetails 같은 JSON 을 성공으로 읽지 않게).
- 본문 없이 온 401·429·400 계열(앞단 인증 미들웨어, 요청 수 제한)은 상태로 `Unauthorized`·`TooManyAttempts`·`BadRequest` 입니다.

## 서버 (`Server~/`)

.NET 10 솔루션 `Server~/Geuneda.Coupon.Server.slnx` 입니다. 게임 서버는 `src/Geuneda.Coupon.Server` 를 `ProjectReference` 로 참조합니다.

```csharp
using Geuneda.Coupon.Server;

builder.Services.AddGeunedaCoupon(options => options.MaxFailures = 10)   // 기본: 1시간에 실패 10번이면 잠금
    .AddStore<DbCouponStore>()            // 게임 DB 저장소(요청 범위). 개발은 .AddInMemoryStore()
    .AddGrantor<WalletGrantor>()          // 게임 지급자(요청 범위, 저장소와 같은 트랜잭션)
    .AddAttemptStore<RedisAttemptStore>() // 서버가 여러 대면 공유 실패 기록(기본 메모리)
    .AddContextResolver<CdnCountryResolver>();   // 선택: 국가·플랫폼을 서버 값으로

var app = builder.Build();
app.MapGeunedaCoupon("/coupons", async context =>
{
    // account 패키지: Bearer 토큰 검증. 실패하면 null → 401
    var header = context.Request.Headers.Authorization.ToString();
    var token = header.StartsWith("Bearer ") ? header.Substring(7) : null;
    var validation = await context.RequestServices.GetRequiredService<IAccessTokenValidator>().ValidateAsync(token, context.RequestAborted);
    return validation.IsValid ? validation.AccountId : null;
});
```

### 캠페인 등록

운영 도구가 `CouponAdmin`(요청 범위)으로 넣습니다. 관리자 인증은 운영 도구 쪽 일입니다.

```csharp
var admin = scope.ServiceProvider.GetRequiredService<CouponAdmin>();

// 공용 코드: 모두가 같은 코드, 계정당 한 번, 전체 한도는 선택
await admin.AddCampaignAsync(new CouponCampaign("welcome-2026", CouponKind.Shared, "WELCOME2026", reward: "{\"gem\":100}",
    startsAt: start, endsAt: end, maxRedemptions: 10000,
    conditions: new CouponConditions(platforms: new[] { "android", "ios" }, countries: new[] { "KR" }, minAppVersion: "1.2.0")));

// 개별 일회용 코드: 코드마다 한 번, 같은 캠페인의 코드는 계정당 하나
await admin.AddCampaignAsync(new CouponCampaign("collab-a", CouponKind.Unique, reward: "{\"skin\":\"collab_a\"}"));
var added = await admin.AddCodesAsync("collab-a", codesFromPartner);   // added.Duplicates: 이미 있어 넣지 않은 코드

// 운영 중 바꾸기(끄기, 기간 연장, 한도, 조건, 보상). 종류·공용 코드는 바꾸지 못함
await admin.UpdateCampaignAsync(new CouponCampaign("welcome-2026", CouponKind.Shared, "WELCOME2026", reward: "{\"gem\":100}", enabled: false));
```

- 공용 코드와 개별 코드는 한 이름 공간입니다(코드 하나로 캠페인 하나를 찾음). 겹치면 `DuplicateCode`·`Duplicates` 로 알립니다.
- 개별 코드는 맞히기 어렵게 만드세요(예: 혼동 글자 `0 O 1 I` 를 뺀 32 글자에서 12자 이상 무작위). 이 패키지는 코드를 만들지 않습니다.
- `reward` 는 지급자가 해석하는 문자열(게임 형식의 JSON 등)입니다.

### 사용 처리 순서

1. **잠금**: 이 계정의 코드 실패(`CouponResultCodes.IsCodeFailure`: 없는 코드·시작 전·끝남·꺼짐·사용됨·한도·조건)가 `FailureWindow`(기본 1시간) 안에 `MaxFailures`(기본 10)번이면 HTTP 429 와 `Retry-After`. 판정 전에 실패 하나를 원자적으로 예약하고 코드 실패가 아니면 지우므로, **동시에 보낸 요청도 진행 중인 것을 합쳐 한도만큼만** 판정에 들어갑니다. 잠긴 동안의 요청·이미 쓴 캠페인·지급 거절·요청 오류·서버 오류는 세지 않습니다.
2. **코드 찾기**: 다듬은 코드로 공용·개별 코드를 찾습니다.
3. **이미 씀**: 이 계정이 이 캠페인을 썼으면 기간·켜짐과 상관없이 처음 결과(`AlreadyRedeemed`)를 돌려줍니다.
4. **켜짐·기간·조건**: 시작 포함·끝 제외, 서버 시각(`TimeProvider`).
5. **저장소 트랜잭션**: 중복 → 개별 코드 사용 → 전체 한도를 확인하고 지급자를 같은 트랜잭션에서 부릅니다. 지급자가 `Granted` 면 기록(지급 결과 JSON 포함)을 커밋하고, `Rejected`·`RetryLater`·예외면 롤백합니다(예외는 500).

### 저장소 구현

`ICouponStore` 의 XML 문서에 표 구성과 동시성 규칙이 있습니다. 요점:

- 캠페인 표(ID 유일, 공용 코드 유일), 개별 코드 표(코드 유일, 쓴 계정), 사용 기록 표((캠페인 ID, 계정 ID) 유일, 지급 결과 JSON).
- 경쟁마다 막는 수단이 다릅니다. **유일 제약은 같은 계정의 중복만 막습니다.** 서로 다른 계정이 마지막 한도를 다투면 둘의 기록은 키가 달라 둘 다 들어가므로, 전체 한도는 캠페인 행 잠금이나 `used < max` 조건부 원자 증가로, 개별 코드는 코드 행 잠금이나 `used_by IS NULL` 조건부 갱신으로 막습니다. 조건부 갱신이 실패하면 게임 지급까지 전부 롤백합니다.
- 롤백 뒤 다시 판정하면 지급자가 한 요청에서 여러 번 불릴 수 있어, 게임 쓰기는 모두 그 트랜잭션 안이어야 합니다.
- 실패 기록(`ICouponAttemptStore`)은 계정마다 배타적으로 세고 넣어야 합니다(DB 계정 행 잠금, Redis Lua 스크립트).
- `InMemoryCouponStore`·`InMemoryCouponAttemptStore` 는 개발·테스트용입니다(다시 시작하면 사라짐, 서버 한 대). **메모리 저장소는 지급을 롤백하지 않습니다.** 지급이 실패하면 사용 기록을 넣지 않을 뿐이라, 지급자가 상태를 바꾼 뒤 예외를 던지면 바뀐 상태가 남습니다.

### 대상 조건의 근거

플랫폼·앱 버전·국가는 클라이언트가 보낸 값이라 **위조할 수 있습니다.** 보상이 큰 조건은 `ICouponContextResolver` 로 서버 값(CDN 의 국가 헤더 `CF-IPCountry`, 계정 DB 의 가입 플랫폼)으로 바꾸거나, 지급자가 계정 정보로 `Rejected` 합니다. 해석기는 계정 ID 를 바꾸면 안 됩니다(500).

### 엔드포인트

`POST {prefix}/redeem`, 본문 `{"code","platform","appVersion","country"}`(camelCase, 대소문자 무시). 응답은 모두 `{"code","message","campaignId","grant","redeemedAt","retryAfterSeconds"}` 이고 401·415·413·400·429·200 입니다. 요청 수 제한(`RequireRateLimiting`) 등은 돌려준 `RouteHandlerBuilder` 에 붙입니다. 다른 통로(실시간 RPC 등)로 받으면 `ICouponRedeemer.RedeemAsync(code, context)` 를 직접 부릅니다.

샘플 서버와 Unity 상호 운용 테스트:

```bash
dotnet run --project "Server~/samples/Geuneda.Coupon.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5699
# Unity: GEUNEDA_COUPON_INTEROP_URL=http://127.0.0.1:5699/coupons/ 로 SampleServerInteropTest 를 이름으로 골라 실행(서버를 새로 띄운 뒤 한 번)
```

## 레퍼런스와 다른 점

레퍼런스는 Lucky Defense 의 Percent `Marketings.Coupon`(+ 웹뷰 플러그인)과 사용자 게임(SpellDefense)의 쿠폰 서비스입니다.

- **따른 것**: 보상은 서버가 지급, 클라이언트는 코드만 보냄, 계정·세션 맥락을 서버에 함께 보냄, 이미 사용·만료·시작 전·비활성·계정당 한도 같은 이유 코드(레퍼런스 `CouponError` 상수 이름에서 추정).
- **보완한 것**:
  - 성공 판정: 레퍼런스는 HTTP 상태를 보지 않고 `code == "SUCCESS"` 하나만 봅니다. SpellDefense 는 서버가 성공에도 `success=false` 를 내려 지급 내역 유무로 판정합니다. 여기서는 결과 코드 하나와 HTTP 200 + 캠페인 ID 를 요구합니다.
  - 제한 시간과 결과를 모를 때의 재시도: 레퍼런스는 제한 시간이 없습니다. 여기서는 제한 시간이 있고, 같은 코드를 다시 보내면 처음 지급 결과가 옵니다.
  - 무작위 대입 방지(계정별 실패 제한)와 사용 기록·지급의 한 트랜잭션: 서버 쪽이라 레퍼런스 클라이언트에서는 확인할 수 없는 부분입니다.
- 웹 페이지 쿠폰은 `com.geuneda.webview` 의 `coupon.redeem` 명령이 이 서비스로 보냅니다. 페이지에 계정 토큰을 넘기지 않으므로 레퍼런스의 `requestPlayerData` 는 두지 않습니다.
