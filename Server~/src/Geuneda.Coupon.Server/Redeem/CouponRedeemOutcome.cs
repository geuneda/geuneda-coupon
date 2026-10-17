namespace Geuneda.Coupon.Server
{
	/// <summary><see cref="ICouponRedeemer.RedeemAsync"/> 의 결과입니다.</summary>
	public sealed class CouponRedeemOutcome
	{
		private CouponRedeemOutcome(int code, string message, CouponRedemptionRecord record, int? retryAfterSeconds)
		{
			Code = code;
			Message = message;
			Record = record;
			RetryAfterSeconds = retryAfterSeconds;
		}

		/// <summary>0 이면 이번에 지급함. 그 밖은 <see cref="CouponResultCodes"/> 의 값입니다.</summary>
		public int Code { get; }

		/// <summary>개발자용 설명. 없으면 null</summary>
		public string Message { get; }

		/// <summary>이번 지급(0)이나 처음 지급(<see cref="CouponResultCodes.AlreadyRedeemed"/>)의 기록. 그 밖은 null</summary>
		public CouponRedemptionRecord Record { get; }

		/// <summary><see cref="CouponResultCodes.TooManyAttempts"/> 일 때 남은 초. 그 밖은 null</summary>
		public int? RetryAfterSeconds { get; }

		internal static CouponRedeemOutcome Redeemed(CouponRedemptionRecord record)
		{
			return new CouponRedeemOutcome(0, null, record, null);
		}

		internal static CouponRedeemOutcome AlreadyRedeemed(CouponRedemptionRecord record)
		{
			return new CouponRedeemOutcome(CouponResultCodes.AlreadyRedeemed, "This account already redeemed the campaign", record, null);
		}

		internal static CouponRedeemOutcome Locked(int retryAfterSeconds)
		{
			return new CouponRedeemOutcome(CouponResultCodes.TooManyAttempts, "Too many failed coupon codes", null, retryAfterSeconds);
		}

		internal static CouponRedeemOutcome Failed(int code, string message)
		{
			return new CouponRedeemOutcome(code, message, null, null);
		}

		public override string ToString()
		{
			return Message == null ? $"Coupon {Code}" : $"Coupon {Code}: {Message}";
		}
	}
}
