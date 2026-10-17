# 실제 DB 저장소 구현 (Postgres, Redis)

`ICouponStore` 와 `ICouponAttemptStore` 를 게임 DB 로 구현할 때의 순서와 예시입니다. 예시는 Postgres(Npgsql 10)와 Redis(StackExchange.Redis 2.x)입니다. 2026-10-17 에 이 문서의 SQL 과 C# 코드를 그대로 뽑아 Postgres 18.3, Redis 8.10 에서 동시 요청 시험을 돌렸습니다(맨 아래 [확인한 결과](#확인한-결과)). 다른 DB 도 원리는 같습니다. 같은 계정의 중복은 사용 기록의 유일 키가 막고, 전체 한도와 개별 코드는 조건부 갱신이나 행 잠금이 막습니다.

## 요점

- **사용 기록 자리를 먼저 차지합니다.** `INSERT ... ON CONFLICT DO NOTHING` → 개별 코드 조건부 갱신 → 사용 수 조건부 증가 → 지급 → 지급 결과 기록 → 커밋 순서입니다. 같은 계정의 동시 요청은 첫 단계에서 줄을 서고, 앞 요청이 커밋하면 그 기록(처음 지급 결과)을 받습니다.
- **조건부 갱신이 0행이라고 바로 `CodeUsed`·`SoldOut` 으로 끝내지 않습니다.** 사용 기록을 맨 뒤에 넣는 순서라면 0행일 때 이 계정의 기록을 다시 읽습니다([대안](#대안-기록을-맨-뒤에-넣는-순서)).
- **지급자는 저장소가 연 트랜잭션에서만 씁니다.** 저장소와 지급자가 요청 범위 세션을 함께 받습니다([지급자](#지급자)).
- **사용 처리가 갱신하는 캠페인 행을 외래 키로 참조하지 않습니다.** Postgres 의 multixact 내부 오류를 피합니다([외래 키를 두지 않는 이유](#외래-키를-두지-않는-이유)).
- 지급 시각은 저장한 값(`RETURNING`)을, 지급 결과 JSON 은 `text` 열을 씁니다.

## 표

```sql
-- 캠페인. used 는 전체 한도의 조건부 증가용 사용 수
CREATE TABLE coupon_campaigns (
    id              text PRIMARY KEY,
    kind            smallint NOT NULL,
    shared_code     text UNIQUE,
    reward          text,
    starts_at       timestamptz,
    ends_at         timestamptz,
    enabled         boolean NOT NULL,
    max_redemptions integer,
    platforms       text[],
    countries       text[],
    min_app_version text,
    used            integer NOT NULL DEFAULT 0
);

-- 개별 일회용 코드. used_by 는 쓴 계정
CREATE TABLE coupon_unique_codes (
    code        text PRIMARY KEY,
    campaign_id text NOT NULL,
    used_by     text
);

-- 사용 기록. (캠페인, 계정) 유일 키가 같은 계정의 중복을 막는다. 지급 결과는 받은 JSON 그대로(text)
CREATE TABLE coupon_redemptions (
    campaign_id text NOT NULL,
    account_id  text NOT NULL,
    code        text NOT NULL,
    redeemed_at timestamptz NOT NULL,
    grant_json  text,
    PRIMARY KEY (campaign_id, account_id)
);

-- 계정별 코드 실패 기록(Postgres 로 둘 때)
CREATE TABLE coupon_attempts (
    id         bigserial PRIMARY KEY,
    account_id text NOT NULL,
    at         timestamptz NOT NULL
);
CREATE INDEX coupon_attempts_account ON coupon_attempts (account_id, at);
```

- 코드와 ID 는 `CouponAdmin` 이 다듬은 값이 옵니다(코드는 대문자 영숫자).
- **공용 코드와 개별 코드는 한 이름 공간입니다.** 위 두 표의 유일 제약은 각자 표 안에서만 겹침을 막습니다. 같은 코드를 공용 캠페인 등록과 개별 코드 추가로 동시에 넣으면 둘 다 들어갈 수 있습니다(시험에서 300쌍 중 4~11개). 아래 예시는 등록을 권고 잠금 하나로 줄 세웁니다. 코드 하나를 기본 키로 하는 표(공용·개별 코드를 함께 담음)를 두면 제약만으로 막을 수 있습니다.
- `grant_json` 을 `jsonb` 로 두면 Postgres 가 키 순서와 공백을 바꿉니다(`{"items":..,"gems":..}` → `{"gems": .., "items": ..}`). 재요청에 처음과 같은 지급 결과를 돌려주려면 `text` 로 둡니다.

### 외래 키를 두지 않는 이유

사용 기록과 개별 코드 표에 `campaign_id REFERENCES coupon_campaigns(id)` 를 두면, 기록을 넣을 때마다 캠페인 행에 `KEY SHARE` 잠금이 걸립니다. 같은 행의 사용 수 갱신과 지급 실패 롤백이 겹치면 Postgres 18.3 이 `ERROR: new multixact has more than one updating member` 를 냅니다. 시험(지급자 1/3 이 롤백하는 요청 100개)에서 외래 키가 있으면 165번 중 5번 이 오류가 났고, 그 요청은 500 이 됐습니다(트랜잭션이 롤백돼 중복 지급은 없음). 외래 키를 빼면 165번 중 0번이었습니다. pgsql-hackers 에 같은 경쟁(외래 키 삽입과 부모 행 갱신의 롤백)을 고치는 패치가 올라와 있습니다([Discard aborted updaters when expanding a multixact](https://www.postgresql.org/message-id/5778D6297D3CCF21+202609151836512678486@vastdata.com.cn), [READ COMMITTED 재현 보고](https://www.postgresql.org/message-id/f5a1d2fb96831f085de71288c66acfab%40evolution-digitale.fr)). 쓰는 판에 반영됐는지 확인하세요.

- 캠페인은 지우는 기능이 없고 `CouponAdmin` 이 캠페인이 있는지 확인한 뒤 코드를 넣으므로 외래 키 없이도 기록이 고아가 되지 않습니다.
- 외래 키가 꼭 필요하면 사용 수를 아무도 참조하지 않는 별도 행(예: `coupon_campaign_counters(campaign_id, used)`)에 두거나 이 오류(SQLSTATE `XX000`)에 요청을 다시 시도합니다. 이 두 방법은 시험하지 않았습니다.

## 요청 범위 세션

`ICouponStore.RedeemAsync` 의 지급 콜백은 트랜잭션을 넘기지 않습니다. 저장소와 지급자가 같은 요청 범위 세션을 받고, 저장소가 연 트랜잭션을 지급자가 세션에서 꺼내 씁니다. 연결은 트랜잭션 동안만 쥡니다. 범위 전체에 쥐면 사용 처리 뒤 실패 기록 저장소가 풀에서 연결을 하나 더 빌립니다.

```csharp
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geuneda.Coupon.Server;
using Npgsql;
using StackExchange.Redis;
```

```csharp
/// <summary>요청 범위 DB 세션입니다. 저장소가 사용 처리 트랜잭션을 열고 같은 범위의 지급자가 그 트랜잭션에 참여합니다.</summary>
public sealed class CouponDbSession : IAsyncDisposable, IDisposable
{
    private NpgsqlConnection _connection;
    private NpgsqlTransaction _transaction;

    public CouponDbSession(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public NpgsqlDataSource DataSource { get; }

    /// <summary>지금 열린 사용 처리 트랜잭션입니다. 지급자는 이것으로만 게임 데이터를 씁니다.</summary>
    public NpgsqlTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException("No coupon transaction is open (the grantor runs only inside ICouponStore.RedeemAsync)");

    public async Task<NpgsqlTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("A coupon transaction is already open in this scope");
        }

        _connection = await DataSource.OpenConnectionAsync(cancellationToken);
        _transaction = await _connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return _transaction;
    }

    /// <summary>트랜잭션을 닫고 연결을 풀에 돌려줍니다. 커밋하지 않았으면 롤백입니다.</summary>
    public async ValueTask EndAsync()
    {
        var transaction = _transaction;
        var connection = _connection;
        _transaction = null;
        _connection = null;

        if (transaction != null)
        {
            await transaction.DisposeAsync();
        }

        if (connection != null)
        {
            await connection.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        return EndAsync();
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
        _transaction = null;
        _connection = null;
    }
}
```

```csharp
/// <summary><c>$1</c>, <c>$2</c> 위치 인자 명령입니다. null 은 DBNull(열 형식으로 추론), 시각은 UTC 로 바꿉니다(Npgsql 은 timestamptz 에 오프셋 0 만 받음).</summary>
internal static class PgCommand
{
    public static NpgsqlCommand Create(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);

        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value is DateTimeOffset time ? time.ToUniversalTime() : value ?? DBNull.Value });
        }

        return command;
    }

    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = Create(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

등록입니다. 저장소와 지급자는 요청 범위(빌더 기본)라 한 요청에서 같은 세션을 받습니다.

```csharp
builder.Services.AddSingleton(NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Game")));
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration["Redis"]));
builder.Services.AddScoped<CouponDbSession>();
builder.Services.AddGeunedaCoupon()
    .AddStore<PgCouponStore>()
    .AddGrantor<WalletGrantor>()
    .AddAttemptStore<RedisCouponAttemptStore>();   // 또는 PgCouponAttemptStore
```

운영 도구가 `CouponAdmin` 을 꺼낼 때는 `await using var scope = app.Services.CreateAsyncScope();` 를 씁니다. 동기 `using var scope = app.Services.CreateScope()` 는 범위를 동기로 해제하므로, `IAsyncDisposable` 만 구현한 요청 범위 서비스가 있으면 `InvalidOperationException` 이 납니다.

## 사용 처리

### 권장 순서: 기록 자리 먼저

1. **기록 자리**: `INSERT INTO coupon_redemptions ... ON CONFLICT (campaign_id, account_id) DO NOTHING RETURNING redeemed_at`. 같은 계정의 동시 요청은 여기서 앞 트랜잭션이 끝날 때까지 기다립니다. 앞이 커밋하면 0행이고, READ COMMITTED 의 다음 문장은 그 기록을 보므로 읽어서 `Duplicate` 입니다. 앞이 롤백하면 이 요청이 자리를 갖습니다.
2. **개별 코드**: `UPDATE coupon_unique_codes SET used_by = $1 WHERE code = $2 AND campaign_id = $3 AND used_by IS NULL`. 0행이면 다른 계정이 쓴 것이라 `CodeUsed` 입니다(같은 계정은 1단계에서 걸러짐).
3. **전체 한도**: `UPDATE coupon_campaigns SET used = used + 1 WHERE id = $1 AND (max_redemptions IS NULL OR used < max_redemptions)`. 0행이면 `SoldOut` 입니다. 한도는 저장된 행의 값이라 운영 중 바꾼 한도가 바로 적용됩니다.
4. **지급**: 같은 트랜잭션에서 지급 콜백을 부릅니다. 지급하지 않았거나 예외면 롤백되고 자리, 코드, 사용 수, 지급자가 쓴 게임 데이터가 함께 되돌아갑니다.
5. **기록**: 지급 결과 JSON 을 기록에 쓰고 커밋합니다. 돌려주는 기록의 시각은 1단계의 `RETURNING` 값입니다. `attempt.RedeemedAt` 을 그대로 돌려주면 첫 응답(100ns 단위)과 재요청 응답(Postgres 는 마이크로초)의 `redeemedAt` 이 달라집니다.

```csharp
/// <summary>Postgres 쿠폰 저장소입니다(요청 범위). 사용 처리는 기록 자리를 먼저 차지합니다.</summary>
public sealed partial class PgCouponStore : ICouponStore
{
    private readonly CouponDbSession _session;

    public PgCouponStore(CouponDbSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
        CancellationToken cancellationToken)
    {
        var transaction = await _session.BeginAsync(cancellationToken);

        try
        {
            var result = await JudgeAsync(transaction, attempt, grant, cancellationToken);

            if (result.Outcome == CouponStoreOutcome.Recorded)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            // 커밋하지 않은 결과(중복, 사용됨, 한도, 미지급)와 예외는 여기서 롤백된다
            await _session.EndAsync();
        }
    }

    private static async Task<CouponStoreRedeemResult> JudgeAsync(NpgsqlTransaction transaction, CouponRedeemAttempt attempt,
        Func<CancellationToken, Task<CouponStoreGrant>> grant, CancellationToken cancellationToken)
    {
        var connection = transaction.Connection;
        var campaignId = attempt.Campaign.Id;
        object claimedAt;

        // 1. 기록 자리. 같은 계정의 요청은 여기서 줄을 선다
        await using (var claim = PgCommand.Create(connection, transaction,
                         "INSERT INTO coupon_redemptions (campaign_id, account_id, code, redeemed_at) VALUES ($1, $2, $3, $4) " +
                         "ON CONFLICT (campaign_id, account_id) DO NOTHING RETURNING redeemed_at",
                         campaignId, attempt.AccountId, attempt.Code, attempt.RedeemedAt))
        {
            claimedAt = await claim.ExecuteScalarAsync(cancellationToken);
        }

        if (claimedAt == null)
        {
            // 앞 요청이 커밋한 기록(처음 지급 결과)
            return CouponStoreRedeemResult.Duplicate(await ReadRedemptionAsync(connection, transaction, campaignId, attempt.AccountId, cancellationToken));
        }

        // 2. 개별 코드
        if (attempt.Campaign.Kind == CouponKind.Unique && await PgCommand.ExecuteAsync(connection, transaction,
                "UPDATE coupon_unique_codes SET used_by = $1 WHERE code = $2 AND campaign_id = $3 AND used_by IS NULL",
                cancellationToken, attempt.AccountId, attempt.Code, campaignId) == 0)
        {
            return CouponStoreRedeemResult.CodeUsed();
        }

        // 3. 전체 한도(캠페인 행 잠금은 커밋까지)
        if (await PgCommand.ExecuteAsync(connection, transaction,
                "UPDATE coupon_campaigns SET used = used + 1 WHERE id = $1 AND (max_redemptions IS NULL OR used < max_redemptions)",
                cancellationToken, campaignId) == 0)
        {
            return CouponStoreRedeemResult.SoldOut();
        }

        // 4. 지급(같은 트랜잭션)
        var granted = await grant(cancellationToken);

        if (!granted.IsGranted)
        {
            return CouponStoreRedeemResult.NotGranted();
        }

        // 5. 지급 결과
        await PgCommand.ExecuteAsync(connection, transaction,
            "UPDATE coupon_redemptions SET grant_json = $1 WHERE campaign_id = $2 AND account_id = $3",
            cancellationToken, granted.GrantJson, campaignId, attempt.AccountId);

        var redeemedAt = new DateTimeOffset(DateTime.SpecifyKind((DateTime) claimedAt, DateTimeKind.Utc));
        return CouponStoreRedeemResult.Recorded(new CouponRedemptionRecord(campaignId, attempt.AccountId, attempt.Code, redeemedAt, granted.GrantJson));
    }

    private static async Task<CouponRedemptionRecord> ReadRedemptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string campaignId,
        string accountId, CancellationToken cancellationToken)
    {
        await using var select = PgCommand.Create(connection, transaction,
            "SELECT code, redeemed_at, grant_json FROM coupon_redemptions WHERE campaign_id = $1 AND account_id = $2", campaignId, accountId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CouponRedemptionRecord(campaignId, accountId, reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }
}
```

### 대안: 기록을 맨 뒤에 넣는 순서

확인(기록 SELECT) → 개별 코드 → 한도 → 지급 → 기록 INSERT 순서로 짤 수도 있습니다. 이때는 두 가지를 지킵니다.

- 2·3단계가 0행이면 같은 트랜잭션에서 이 계정의 기록을 다시 읽고, 있으면 `Duplicate` 로 돌려줍니다. 같은 계정의 앞 요청이 그 코드나 마지막 한도를 방금 가져간 경우입니다. `SELECT ... FOR UPDATE` 로 캠페인·코드 행을 잠그는 방식이면 잠근 뒤에 기록을 확인합니다.
- 기록 INSERT 가 유일 키 위반(`23505`)이면 지급까지 전부 롤백하고 처음부터 다시 판정합니다(다음 판정은 `Duplicate`).

```csharp
if (await PgCommand.ExecuteAsync(connection, transaction, increment, cancellationToken, campaignId) == 0)
{
    // 바로 SoldOut 으로 끝내지 않는다. 같은 계정의 앞 요청이 마지막 한도를 가져갔을 수 있다
    var existing = await ReadRedemptionAsync(connection, transaction, campaignId, attempt.AccountId, cancellationToken);
    return existing != null ? CouponStoreRedeemResult.Duplicate(existing) : CouponStoreRedeemResult.SoldOut();
}
```

이 순서는 같은 계정의 중복 요청마다 지급 콜백을 불렀다가 롤백합니다(시험에서 요청 200개, 기록 20개에 콜백 165~175번). 지급자가 트랜잭션 밖 부작용을 가지면 그만큼 일어납니다.

### 하지 말 것: 0행을 바로 실패로

기록을 맨 뒤에 넣으면서 조건부 갱신 0행을 바로 `SoldOut`·`CodeUsed` 로 끝내면 같은 계정의 동시 요청이 처음 지급 결과 대신 실패를 받습니다. 앞 요청이 행을 잠근 채 지급하는 동안 뒤 요청이 기다렸다가, 앞이 커밋한 뒤 0행을 보기 때문입니다.

- 시험: 남은 한도 1 에서 같은 계정 50개 동시 요청이면 `SoldOut` 4~17개, 같은 개별 코드로 50개면 `CodeUsed` 12~24개였습니다(기대는 지급 1, `AlreadyRedeemed` 49).
- 영향: 이미 받은 계정이 "매진"·"다른 사람이 쓴 코드" 안내를 받습니다. 두 결과는 코드 실패(`CouponResultCodes.IsCodeFailure`)라 계정별 실패 제한에도 들어가 잠금을 앞당깁니다.

### 사용 수를 언제 늘리나

조건부 증가는 캠페인 행을 커밋까지 잠급니다.

- **지급 전(위 예시)**: 잠금을 지급 시간 내내 쥐므로 한 캠페인의 사용 처리가 한 줄로 섭니다. 시험에서 한도 없는 공용 캠페인에 계정 200개, 지급 10ms 가 2.6~4.5초 걸렸습니다. 한도에 닿은 요청은 지급자를 부르지 않습니다.
- **지급 뒤(커밋 직전)**: 잠금이 짧아 같은 시험이 0.2~1.4초였습니다. 대신 한도 직전에는 결국 `SoldOut` 이 될 요청도 지급자를 불렀다가 롤백합니다(한도 50에 요청 300개면 콜백 300번). 3단계를 4단계 뒤로 옮기면 됩니다.
- **한도 없는 캠페인**: `max_redemptions` 가 없으면 사용 수를 세지 않는 선택도 있습니다. 운영 중에 한도를 넣을 때 `used` 를 기록 수로 다시 맞춰야 하고, 그 사이 진행 중인 요청만큼 넘을 수 있습니다. 이 선택은 시험하지 않았습니다.

## 지급자

지급자는 세션의 트랜잭션으로 게임 데이터를 씁니다. 보상 형식은 게임이 정합니다(여기서는 `Reward` 가 젬 개수).

```sql
-- 예시 지급자(WalletGrantor)가 쓰는 지갑. 게임의 표로 바꾼다
CREATE TABLE game_wallets (
    account_id text PRIMARY KEY,
    gems       integer NOT NULL
);
```

```csharp
/// <summary>보상(젬 개수)을 지갑 표에 더하는 지급자 예시입니다(요청 범위). 저장소가 연 트랜잭션 안에서만 씁니다.</summary>
public sealed class WalletGrantor : ICouponGrantor
{
    private readonly CouponDbSession _session;

    public WalletGrantor(CouponDbSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<CouponGrantResult> GrantAsync(CouponGrant grant, CancellationToken cancellationToken)
    {
        var transaction = _session.Transaction;
        var gems = int.Parse(grant.Campaign.Reward ?? "0", CultureInfo.InvariantCulture);

        if (gems <= 0)
        {
            return CouponGrantResult.Rejected("No reward configured");
        }

        await using var wallet = PgCommand.Create(transaction.Connection, transaction,
            "INSERT INTO game_wallets (account_id, gems) VALUES ($1, $2) ON CONFLICT (account_id) DO UPDATE SET gems = game_wallets.gems + EXCLUDED.gems RETURNING gems",
            grant.AccountId, gems);
        var balance = (int) await wallet.ExecuteScalarAsync(cancellationToken);
        return CouponGrantResult.Granted(new { gems, balance });
    }
}
```

- **트랜잭션 밖 부작용은 롤백되지 않습니다.** 외부 API 호출, 메모리 상태, 메시지 발행, 다른 DB 쓰기는 지급 콜백이 롤백돼도 남습니다. 지급 콜백은 롤백되는 호출이 생길 수 있습니다(지급 뒤 사용 수 증가의 한도 경쟁, 기록을 맨 뒤에 넣는 순서의 중복 요청, 유일 키 위반 뒤 다시 판정). 이런 일은 같은 트랜잭션에 넣은 아웃박스 행 등으로 커밋 뒤에 합니다.
- 지급자 결과를 JSON 으로 저장해 재요청에 그대로 돌려줍니다. 잔액처럼 그때의 값은 재요청에서도 처음 값입니다.

## 운영 입구와 코드 찾기

나머지 `ICouponStore` 메서드입니다. 캠페인 등록과 개별 코드 추가는 권고 잠금 하나로 줄 세워 공용·개별 코드의 한 이름 공간을 지킵니다(운영 작업이라 드묾).

```csharp
public sealed partial class PgCouponStore
{
    private const string CampaignColumns =
        "c.id, c.kind, c.shared_code, c.reward, c.starts_at, c.ends_at, c.enabled, c.max_redemptions, c.platforms, c.countries, c.min_app_version";

    public async Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken)
    {
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockCodeNamespaceAsync(connection, transaction, cancellationToken);

        if (await ExistsAsync(connection, transaction, "SELECT 1 FROM coupon_campaigns WHERE id = $1", cancellationToken, campaign.Id))
        {
            return CouponAddResult.DuplicateId;
        }

        if (campaign.SharedCode != null && await ExistsAsync(connection, transaction,
                "SELECT 1 FROM coupon_campaigns WHERE shared_code = $1 UNION ALL SELECT 1 FROM coupon_unique_codes WHERE code = $1",
                cancellationToken, campaign.SharedCode))
        {
            return CouponAddResult.DuplicateCode;
        }

        var conditions = campaign.Conditions;
        await PgCommand.ExecuteAsync(connection, transaction,
            "INSERT INTO coupon_campaigns (id, kind, shared_code, reward, starts_at, ends_at, enabled, max_redemptions, platforms, countries, min_app_version) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)", cancellationToken,
            campaign.Id, (short) campaign.Kind, campaign.SharedCode, campaign.Reward, campaign.StartsAt, campaign.EndsAt, campaign.Enabled,
            campaign.MaxRedemptions, ToArrayOrNull(conditions.Platforms), ToArrayOrNull(conditions.Countries), conditions.MinAppVersion);
        await transaction.CommitAsync(cancellationToken);
        return CouponAddResult.Added;
    }

    public async Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken)
    {
        var conditions = campaign.Conditions;
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        return await PgCommand.ExecuteAsync(connection, null,
            "UPDATE coupon_campaigns SET reward = $2, starts_at = $3, ends_at = $4, enabled = $5, max_redemptions = $6, platforms = $7, countries = $8, " +
            "min_app_version = $9 WHERE id = $1", cancellationToken,
            campaign.Id, campaign.Reward, campaign.StartsAt, campaign.EndsAt, campaign.Enabled, campaign.MaxRedemptions,
            ToArrayOrNull(conditions.Platforms), ToArrayOrNull(conditions.Countries), conditions.MinAppVersion) > 0;
    }

    public async Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken)
    {
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        await using var select = PgCommand.Create(connection, null, $"SELECT {CampaignColumns} FROM coupon_campaigns c WHERE c.id = $1", campaignId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCampaign(reader) : null;
    }

    public async Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockCodeNamespaceAsync(connection, transaction, cancellationToken);

        if (!await ExistsAsync(connection, transaction, "SELECT 1 FROM coupon_campaigns WHERE id = $1", cancellationToken, campaignId))
        {
            throw new InvalidOperationException($"Campaign {campaignId} does not exist");
        }

        var added = new HashSet<string>(StringComparer.Ordinal);

        await using (var insert = PgCommand.Create(connection, transaction,
                         "INSERT INTO coupon_unique_codes (code, campaign_id) SELECT x, $1 FROM unnest($2::text[]) AS x " +
                         "WHERE NOT EXISTS (SELECT 1 FROM coupon_campaigns WHERE shared_code = x) ON CONFLICT (code) DO NOTHING RETURNING code",
                         campaignId, codes.ToArray()))
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                added.Add(reader.GetString(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new CouponCodesAddResult(added.Count, codes.Where(code => !added.Contains(code)).ToList().AsReadOnly());
    }

    public async Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        await using var select = PgCommand.Create(connection, null,
            $"SELECT {CampaignColumns}, c.shared_code FROM coupon_campaigns c WHERE c.shared_code = $1 " +
            $"UNION ALL SELECT {CampaignColumns}, u.code FROM coupon_unique_codes u JOIN coupon_campaigns c ON c.id = u.campaign_id WHERE u.code = $1 LIMIT 1",
            code);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new CouponCodeMatch(ReadCampaign(reader), reader.GetString(11)) : null;
    }

    public async Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken)
    {
        await using var connection = await _session.DataSource.OpenConnectionAsync(cancellationToken);
        return await ReadRedemptionAsync(connection, null, campaignId, accountId, cancellationToken);
    }

    private static async Task LockCodeNamespaceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await PgCommand.ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(hashtext('coupon-code-namespace'))", cancellationToken);
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = PgCommand.Create(connection, transaction, sql, values);
        return await command.ExecuteScalarAsync(cancellationToken) != null;
    }

    private static string[] ToArrayOrNull(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? null : values.ToArray();
    }

    private static CouponCampaign ReadCampaign(NpgsqlDataReader reader)
    {
        var platforms = reader.IsDBNull(8) ? null : reader.GetFieldValue<string[]>(8);
        var countries = reader.IsDBNull(9) ? null : reader.GetFieldValue<string[]>(9);
        var minAppVersion = reader.IsDBNull(10) ? null : reader.GetString(10);
        var conditions = platforms == null && countries == null && minAppVersion == null ? null : new CouponConditions(platforms, countries, minAppVersion);

        return new CouponCampaign(reader.GetString(0), (CouponKind) reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? (DateTimeOffset?) null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? (DateTimeOffset?) null : reader.GetFieldValue<DateTimeOffset>(5), reader.GetBoolean(6),
            reader.IsDBNull(7) ? (int?) null : reader.GetInt32(7), conditions);
    }
}
```

## 실패 기록

사용 요청은 판정 전에 실패 하나를 예약하고 코드 실패가 아니면 지웁니다(`ICouponAttemptStore`). 예약은 계정마다 배타적으로 세고 넣어야 하고, 사용 처리 트랜잭션과 따로 바로 커밋합니다(다른 요청이 봐야 함). 서버가 여러 대면 모두 같은 저장소를 씁니다(메모리 기록이면 서버 대수만큼 더 시도할 수 있음).

- 진행 중인 요청도 한도에 들어갑니다. 한 계정이 `MaxFailures` 보다 많은 요청을 동시에 보내면 실패가 없어도 일부는 429 입니다(샘플 설정 5 에서 같은 계정 60개 동시 요청 중 7개). 실패가 한도 바로 밑이면 맞는 코드도 함께 보낸 틀린 코드에 자리를 뺏겨 429 일 수 있습니다.

### Postgres(권고 잠금)

```csharp
/// <summary>Postgres 실패 기록입니다(싱글턴). 계정마다 트랜잭션 권고 잠금을 잡고 지난 기록 지우기, 세기, 넣기를 합니다.</summary>
public sealed class PgCouponAttemptStore : ICouponAttemptStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PgCouponAttemptStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<CouponAttemptReservation> TryReserveAsync(string accountId, DateTimeOffset at, DateTimeOffset since, int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 42 는 이 저장소의 잠금 번호(다른 권고 잠금과 겹치지 않게). 커밋하면 풀린다
        await PgCommand.ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(42, hashtext($1))", cancellationToken, accountId);
        await PgCommand.ExecuteAsync(connection, transaction, "DELETE FROM coupon_attempts WHERE account_id = $1 AND at < $2", cancellationToken,
            accountId, since);

        var entries = new List<DateTimeOffset>();

        await using (var select = PgCommand.Create(connection, transaction, "SELECT at FROM coupon_attempts WHERE account_id = $1", accountId))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(reader.GetFieldValue<DateTimeOffset>(0));
            }
        }

        if (entries.Count >= limit)
        {
            await transaction.CommitAsync(cancellationToken);
            return CouponAttemptReservation.Denied(entries.AsReadOnly());
        }

        long id;

        await using (var insert = PgCommand.Create(connection, transaction, "INSERT INTO coupon_attempts (account_id, at) VALUES ($1, $2) RETURNING id",
                         accountId, at))
        {
            id = (long) await insert.ExecuteScalarAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CouponAttemptReservation.Reserved(id.ToString(CultureInfo.InvariantCulture));
    }

    public async Task ReleaseAsync(string accountId, string reservationId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(reservationId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PgCommand.ExecuteAsync(connection, null, "DELETE FROM coupon_attempts WHERE account_id = $1 AND id = $2", cancellationToken, accountId, id);
    }
}
```

### Redis(Lua 와 정렬 집합)

계정마다 정렬 집합 하나(점수는 시각의 유닉스 마이크로초, 멤버는 예약 ID)이고, 지난 기록 지우기·세기·넣기를 Lua 스크립트 하나로 원자적으로 합니다. 키가 하나라 Redis Cluster 에서도 됩니다. 키는 창 길이가 지나면 사라집니다. 점수를 .NET 틱(100ns)으로 두면 double 정밀도를 넘으므로 마이크로초로 둡니다.

```csharp
/// <summary>Redis 실패 기록입니다(싱글턴).</summary>
public sealed class RedisCouponAttemptStore : ICouponAttemptStore
{
    // KEYS[1] 계정 키, ARGV: at(us), since(us), limit, 예약 ID, 키 수명(ms)
    private const string ReserveScript = @"
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', '(' .. ARGV[2])
if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
  return redis.call('ZRANGE', KEYS[1], 0, -1, 'WITHSCORES')
end
redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
redis.call('PEXPIRE', KEYS[1], ARGV[5])
return ARGV[4]
";

    private readonly IDatabase _database;
    private readonly string _prefix;

    public RedisCouponAttemptStore(IConnectionMultiplexer redis, string prefix = "coupon:attempts:")
    {
        _database = (redis ?? throw new ArgumentNullException(nameof(redis))).GetDatabase();
        _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    }

    public async Task<CouponAttemptReservation> TryReserveAsync(string accountId, DateTimeOffset at, DateTimeOffset since, int limit,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var ttl = (long) Math.Ceiling((at - since).TotalMilliseconds) + 1000L;
        var result = await _database.ScriptEvaluateAsync(ReserveScript, new RedisKey[] { _prefix + accountId },
            new RedisValue[] { ToMicros(at), ToMicros(since), limit, id, ttl });

        if (result.Resp2Type == ResultType.BulkString)
        {
            return CouponAttemptReservation.Reserved((string) result);
        }

        // 거절: [멤버, 점수, 멤버, 점수, ...]
        var pairs = (RedisResult[]) result;
        var entries = new List<DateTimeOffset>(pairs.Length / 2);

        for (var i = 1; i < pairs.Length; i += 2)
        {
            entries.Add(DateTimeOffset.UnixEpoch.AddTicks((long) (double) pairs[i] * 10));
        }

        return CouponAttemptReservation.Denied(entries.AsReadOnly());
    }

    public Task ReleaseAsync(string accountId, string reservationId, CancellationToken cancellationToken)
    {
        return _database.SortedSetRemoveAsync(_prefix + accountId, reservationId);
    }

    private static long ToMicros(DateTimeOffset value)
    {
        return (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;
    }
}
```

## 운영 주의

- **켜짐·기간·조건은 트랜잭션 밖에서 판정합니다.** `CouponRedeemer` 는 `FindCodeAsync` 로 읽은 캠페인으로 켜짐·기간·대상 조건을 본 뒤 저장소 트랜잭션에 들어갑니다. 운영자가 캠페인을 끈 직후에도 이미 그 판정을 지난 요청은 지급되고, 지급자가 받는 `Campaign.Reward` 도 그때 읽은 값입니다. 저장소의 결과에는 "지금 꺼짐"이 없습니다. 끄기·보상 변경을 즉시 막아야 하면 지급자가 트랜잭션 안에서 캠페인 행을 다시 읽어 `Rejected` 합니다.
- **전체 한도는 저장된 행의 한도로 셉니다.** 운영 중 한도를 줄여도 이미 쓴 수 아래로는 내려가지 않고, 그 뒤 요청은 `SoldOut` 입니다.
- 실패 기록 예약이 남아 있는 동안 서버가 죽으면 그 예약은 실패로 남습니다(보수적).

## 확인한 결과

2026-10-17, Postgres 18.3(READ COMMITTED), Redis 8.10, Npgsql 10.0.3, StackExchange.Redis 2.13.17, .NET 10. 위 표·코드를 이 문서에서 그대로 뽑아 실제 Kestrel 에 `MapGeunedaCoupon` 을 붙이고 HTTP 로 보냈습니다. 지급자는 같은 트랜잭션에서 지갑 표를 쓰고, 시험에 따라 지갑을 쓴 뒤 예외·거절·보류합니다.

| 시험 | 요청 | 기대 | 이 문서의 구현 |
|------|------|------|----------------|
| 같은 계정이 같은 공용 캠페인을 동시에 | 계정 20 x 10 | 계정마다 지급 1, `AlreadyRedeemed` 9, 같은 지급 결과·시각, 지갑에 1번 | 같음. 지급자 호출 20번 |
| 남은 한도 1 에서 같은 계정이 동시에 | 50 | 지급 1, `AlreadyRedeemed` 49 | 같음. 0행을 바로 실패로 끝내는 순서는 `SoldOut` 4~17 |
| 같은 계정이 같은 개별 코드로 동시에 | 50 | 지급 1, `AlreadyRedeemed` 49 | 같음. 0행을 바로 실패로 끝내는 순서는 `CodeUsed` 12~24 |
| 한도 50 에 여러 계정이 동시에 | 300 | 지급 50, `SoldOut` 250, 사용 수 50 | 같음(0.5~1.0초) |
| 개별 코드 20개를 계정 10개씩 다툼 | 200 | 코드마다 한 번, 기록과 쓴 계정 일치 | 같음 |
| 지급자가 지갑을 쓴 뒤 예외·거절·보류(1/3씩), 한도 30 | 100 | 정상 계정만 정확히 30 지급, 고장 계정 지갑 0, 예외는 실패로 세지 않음 | 같음 |
| 같은 고장, 개별 코드 40개를 계정 3개씩 | 120 | 코드마다 정상 계정이 받음 | 같음 |
| 서버를 다시 띄운 뒤, 끈 뒤, 기간이 끝난 뒤 같은 계정 | 순차 | 처음 지급 결과와 시각, 지급자 1번 | 같음 |
| 공용·개별 같은 코드를 동시에 등록 | 300쌍 | 한쪽에만 들어감 | 같음. 잠금이 없으면 4~11쌍이 양쪽 |
| 한도 없는 캠페인, 지급 10ms | 계정 200 | 모두 지급 | 같음. 2.6~4.5초(사용 수를 지급 뒤에 늘리면 0.2~1.4초) |
| 실패 기록: 같은 계정의 틀린 코드(1시간 10번) | 100 | 판정 10, 429 90, `Retry-After` 3600 | Postgres·Redis 같음 |
| 실패 기록: 서버 두 대가 나눠 받음 | 120 | 합쳐서 판정 10 | Postgres·Redis 10(메모리 기록은 20) |
| 실패 9번 뒤 맞는 코드 1 + 틀린 코드 30 동시 | 31 x 5번 | 판정된 실패 1 이하, 기록 10 이하 | 같음(맞는 코드는 대개 429) |
| 지급자 예외·거절을 동시에, 그 뒤 순차 | 40 + 30 | 실패로 세지 않음, 잠기지 않음 | Postgres·Redis 같음 |
| 패키지 서버 테스트 31개 + 실패 기록 계약 시험 | 32 | 모두 통과 | Postgres·Redis 실패 기록 모두 통과. 0행을 바로 실패로 끝내는 순서는 `ConcurrentRequests_GrantOnce` 에서 `SoldOut` 으로 실패 |
