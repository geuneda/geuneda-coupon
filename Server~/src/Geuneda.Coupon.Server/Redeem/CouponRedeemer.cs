using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.Coupon.Server
{
	/// <summary>기본 사용 처리입니다. 규칙은 <see cref="ICouponRedeemer"/> 를 봅니다.</summary>
	internal sealed class CouponRedeemer : ICouponRedeemer
	{
		private readonly ICouponStore _store;
		private readonly ICouponGrantor _grantor;
		private readonly ICouponAttemptStore _attempts;
		private readonly TimeProvider _time;
		private readonly CouponServerOptions _options;
		private readonly ILogger<CouponRedeemer> _logger;

		public CouponRedeemer(ICouponStore store, ICouponGrantor grantor, ICouponAttemptStore attempts, TimeProvider time,
			IOptions<CouponServerOptions> options, ILogger<CouponRedeemer> logger)
		{
			_store = store;
			_grantor = grantor;
			_attempts = attempts;
			_time = time;
			_options = options.Value;
			_logger = logger;
		}

		public async Task<CouponRedeemOutcome> RedeemAsync(string code, CouponContext context, CancellationToken cancellationToken)
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			var now = _time.GetUtcNow();

			// 판정 전에 실패 하나를 미리 기록한다. 확인과 기록이 한 번에 일어나야 동시 요청이 함께 한도를 지나가지 못한다
			var reservation = await _attempts.TryReserveAsync(context.AccountId, now, now - _options.FailureWindow, _options.MaxFailures, cancellationToken);

			if (!reservation.IsReserved)
			{
				// 예약 수는 진행 중 요청을 포함한 그때의 값이라, 확정 실패 수로 알리지 않고 실제로 거절한 이곳에서만 남긴다
				_logger.LogWarning("Rejected coupon redemption for account {Account}: {Count} failed or in-flight codes within {Window}",
					context.AccountId, reservation.Entries.Count, _options.FailureWindow);
				return CouponRedeemOutcome.Locked(RetryAfterSeconds(reservation.Entries, now));
			}

			CouponRedeemOutcome outcome = null;

			try
			{
				outcome = await JudgeAsync(code, context, now, cancellationToken);
				return outcome;
			}
			finally
			{
				// 코드 실패면 예약이 실패 기록으로 남는다. 아니거나 판정 전에 끝났으면(예외, 취소) 지운다. 요청 토큰을 넘기지 않는다(취소된 요청이 예약을 남기지 않게)
				if (outcome == null || !CouponResultCodes.IsCodeFailure(outcome.Code))
				{
					await _attempts.ReleaseAsync(context.AccountId, reservation.ReservationId, CancellationToken.None);
				}
			}
		}

		private async Task<CouponRedeemOutcome> JudgeAsync(string code, CouponContext context, DateTimeOffset now, CancellationToken cancellationToken)
		{
			if (!CouponCodes.TryNormalize(code, out var normalized))
			{
				return CouponRedeemOutcome.Failed(CouponResultCodes.InvalidCode, "Not a coupon code");
			}

			var match = await _store.FindCodeAsync(normalized, cancellationToken);

			if (match == null)
			{
				return CouponRedeemOutcome.Failed(CouponResultCodes.InvalidCode, "Unknown coupon code");
			}

			var campaign = match.Campaign;

			// 끝난 캠페인이어도 이미 쓴 계정에는 처음 결과를 돌려준다(응답을 잃고 다시 보낸 경우)
			var existing = await _store.FindRedemptionAsync(campaign.Id, context.AccountId, cancellationToken);

			if (existing != null)
			{
				return CouponRedeemOutcome.AlreadyRedeemed(existing);
			}

			var availability = campaign.CheckAvailability(now);

			if (availability != 0)
			{
				return CouponRedeemOutcome.Failed(availability, $"{campaign} is not available");
			}

			if (!campaign.Conditions.Allows(context))
			{
				return CouponRedeemOutcome.Failed(CouponResultCodes.NotEligible, $"{campaign} does not allow {context}");
			}

			// 저장소가 롤백 뒤 다시 부를 수 있으므로 결과는 마지막 시도의 것을 쓴다
			var decision = default(CouponGrantResult);
			var attempt = new CouponRedeemAttempt(campaign, match.Code, context, now);
			var result = await _store.RedeemAsync(attempt, async token =>
			{
				decision = await _grantor.GrantAsync(new CouponGrant(campaign, match.Code, context, now), token);

				switch (decision.Status)
				{
					case CouponGrantStatus.Granted:
						return CouponStoreGrant.Granted(decision.Response == null
							? null
							: JsonSerializer.Serialize(decision.Response, decision.Response.GetType(), CouponServerJson.Options));
					case CouponGrantStatus.Rejected:
					case CouponGrantStatus.RetryLater:
						return CouponStoreGrant.NotGranted();
					default:
						throw new InvalidOperationException($"Coupon grantor returned an undefined result ({decision.Status}) for {campaign}");
				}
			}, cancellationToken);

			switch (result.Outcome)
			{
				case CouponStoreOutcome.Recorded:
					_logger.LogInformation("Redeemed {Campaign} code {Code} for account {Account}", campaign.Id, match.Code, context.AccountId);
					return CouponRedeemOutcome.Redeemed(result.Record);
				case CouponStoreOutcome.Duplicate:
					return CouponRedeemOutcome.AlreadyRedeemed(result.Record);
				case CouponStoreOutcome.CodeUsed:
					return CouponRedeemOutcome.Failed(CouponResultCodes.CodeUsed, "The code was used by another account");
				case CouponStoreOutcome.SoldOut:
					return CouponRedeemOutcome.Failed(CouponResultCodes.SoldOut, $"{campaign} reached its redemption limit");
				case CouponStoreOutcome.NotGranted:
					return decision.Status == CouponGrantStatus.Rejected
						? CouponRedeemOutcome.Failed(CouponResultCodes.GrantRejected, decision.Message)
						: CouponRedeemOutcome.Failed(CouponResultCodes.GrantDeferred, decision.Message);
				default:
					throw new InvalidOperationException($"Coupon store returned an undefined outcome ({result.Outcome}) for {campaign}");
			}
		}

		/// <summary>
		/// 창 안의 실패가 한도 밑으로 내려갈 때까지(한도번째로 최근 실패가 창 밖으로 나갈 때까지) 남은 초, 1 이상입니다. 창 경계의 실패는 아직 세므로(<c>since</c> 포함)
		/// 그 시각을 지난 첫 초를 돌려줍니다.
		/// </summary>
		private int RetryAfterSeconds(IReadOnlyList<DateTimeOffset> failures, DateTimeOffset now)
		{
			var sorted = new List<DateTimeOffset>(failures);
			sorted.Sort();
			var releaseAt = sorted[sorted.Count - _options.MaxFailures] + _options.FailureWindow;
			return Math.Max(1, (int) Math.Floor((releaseAt - now).TotalSeconds) + 1);
		}
	}
}
