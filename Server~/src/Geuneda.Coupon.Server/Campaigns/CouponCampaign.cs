using System;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 쿠폰 캠페인입니다. 만든 뒤 바뀌지 않습니다. 운영 중 바꿀 값(켜기·끄기, 기간, 한도, 조건, 보상)은 새 인스턴스로 <see cref="CouponAdmin.UpdateCampaignAsync"/> 합니다.
	/// </summary>
	public sealed class CouponCampaign
	{
		/// <param name="id">캠페인 ID. 1~128자 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>. 사용 기록의 키라 바꾸지 않습니다</param>
		/// <param name="kind">종류</param>
		/// <param name="sharedCode"><see cref="CouponKind.Shared"/> 의 코드(필수, <see cref="CouponCodes.TryNormalize"/> 로 바꿔 둠). <see cref="CouponKind.Unique"/> 는 null(코드는 따로 등록)</param>
		/// <param name="reward">지급자가 해석하는 보상 정의(게임 형식의 JSON 등). 없으면 null</param>
		/// <param name="startsAt">쓸 수 있게 되는 시각(포함). null 이면 처음부터</param>
		/// <param name="endsAt">쓸 수 없게 되는 시각(제외). null 이면 끝없음</param>
		/// <param name="enabled">false 면 기간과 상관없이 막습니다(<see cref="CouponResultCodes.Disabled"/>)</param>
		/// <param name="maxRedemptions">캠페인 전체 사용 한도. null 이면 없음(개별 코드는 코드 수가 한도)</param>
		/// <param name="conditions">대상 조건. null 이면 없음</param>
		/// <exception cref="ArgumentException">값이 규칙에 맞지 않는 경우</exception>
		public CouponCampaign(string id, CouponKind kind, string sharedCode = null, string reward = null, DateTimeOffset? startsAt = null,
			DateTimeOffset? endsAt = null, bool enabled = true, int? maxRedemptions = null, CouponConditions conditions = null)
		{
			if (!CouponNames.IsId(id))
			{
				throw new ArgumentException($"Campaign id must be 1-{CouponNames.MaxIdLength} letters, digits, '.', '_' or '-': '{id}'", nameof(id));
			}

			if (kind != CouponKind.Shared && kind != CouponKind.Unique)
			{
				throw new ArgumentException($"Undefined campaign kind {kind}", nameof(kind));
			}

			if (kind == CouponKind.Shared)
			{
				if (!CouponCodes.TryNormalize(sharedCode, out var normalized))
				{
					throw new ArgumentException(
						$"A shared campaign needs a code of {CouponCodes.MinLength}-{CouponCodes.MaxLength} letters and digits (spaces and '-' are ignored): '{sharedCode}'",
						nameof(sharedCode));
				}

				SharedCode = normalized;
			}
			else if (sharedCode != null)
			{
				throw new ArgumentException("A unique campaign takes its codes from AddCodesAsync, not sharedCode", nameof(sharedCode));
			}

			if (startsAt.HasValue && endsAt.HasValue && endsAt.Value <= startsAt.Value)
			{
				throw new ArgumentException("endsAt must be later than startsAt", nameof(endsAt));
			}

			if (maxRedemptions.HasValue && maxRedemptions.Value < 1)
			{
				throw new ArgumentException("maxRedemptions must be at least 1", nameof(maxRedemptions));
			}

			Id = id;
			Kind = kind;
			Reward = reward;
			StartsAt = startsAt;
			EndsAt = endsAt;
			Enabled = enabled;
			MaxRedemptions = maxRedemptions;
			Conditions = conditions ?? CouponConditions.None;
		}

		public string Id { get; }

		public CouponKind Kind { get; }

		/// <summary><see cref="CouponKind.Shared"/> 의 비교용 코드입니다. 개별 코드 캠페인은 null 입니다.</summary>
		public string SharedCode { get; }

		public string Reward { get; }

		public DateTimeOffset? StartsAt { get; }

		public DateTimeOffset? EndsAt { get; }

		public bool Enabled { get; }

		public int? MaxRedemptions { get; }

		public CouponConditions Conditions { get; }

		/// <summary>지금 쓸 수 있는 상태가 아니면 그 결과 코드(<see cref="CouponResultCodes.Disabled"/>, <see cref="CouponResultCodes.NotStarted"/>, <see cref="CouponResultCodes.Expired"/>), 쓸 수 있으면 0 입니다.</summary>
		public int CheckAvailability(DateTimeOffset now)
		{
			if (!Enabled)
			{
				return CouponResultCodes.Disabled;
			}

			if (StartsAt.HasValue && now < StartsAt.Value)
			{
				return CouponResultCodes.NotStarted;
			}

			return EndsAt.HasValue && now >= EndsAt.Value ? CouponResultCodes.Expired : 0;
		}

		public override string ToString()
		{
			return $"Campaign {Id} ({Kind})";
		}
	}
}
