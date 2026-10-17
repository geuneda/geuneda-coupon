namespace Geuneda.Coupon.Server
{
	/// <summary>지급자 결과의 종류입니다.</summary>
	public enum CouponGrantStatus
	{
		/// <summary>지급했습니다. 사용 기록을 넣고 커밋합니다</summary>
		Granted = 1,

		/// <summary>지급할 수 없습니다(계정 조건 등). 기록하지 않습니다(<see cref="CouponResultCodes.GrantRejected"/>)</summary>
		Rejected = 2,

		/// <summary>지금은 지급할 수 없습니다. 기록하지 않고 클라이언트가 나중에 다시 보냅니다(<see cref="CouponResultCodes.GrantDeferred"/>)</summary>
		RetryLater = 3,
	}

	/// <summary>지급자(<see cref="ICouponGrantor"/>)의 결과입니다. <c>default</c> 는 정의되지 않은 값이라 사용 요청이 예외로 끝납니다.</summary>
	public readonly struct CouponGrantResult
	{
		private CouponGrantResult(CouponGrantStatus status, object response, string message)
		{
			Status = status;
			Response = response;
			Message = message;
		}

		public CouponGrantStatus Status { get; }

		/// <summary>
		/// 클라이언트에 돌려줄 지급 결과(보상 목록 등, <see cref="CouponRedeemResponse{TGrant}.Grant"/>). JSON 으로 사용 기록에 함께 저장해 같은 계정의 재요청에도 돌려줍니다. 없으면 null
		/// </summary>
		public object Response { get; }

		/// <summary>로그·응답용 설명. 없으면 null</summary>
		public string Message { get; }

		public static CouponGrantResult Granted(object response = null)
		{
			return new CouponGrantResult(CouponGrantStatus.Granted, response, null);
		}

		public static CouponGrantResult Rejected(string message = null)
		{
			return new CouponGrantResult(CouponGrantStatus.Rejected, null, message);
		}

		public static CouponGrantResult RetryLater(string message = null)
		{
			return new CouponGrantResult(CouponGrantStatus.RetryLater, null, message);
		}

		public override string ToString()
		{
			return Message == null ? Status.ToString() : $"{Status}: {Message}";
		}
	}
}
