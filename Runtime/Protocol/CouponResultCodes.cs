// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 쿠폰 결과 코드입니다(core 결과 코드 범위 표의 11000 블록). 서버 응답(<see cref="CouponRedeemResponse{TGrant}.Code"/>)과 클라이언트 <c>Result</c> 실패 코드가 같은 값입니다.
	/// 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public static class CouponResultCodes
	{
		/// <summary>없는 코드이거나 코드 형식이 아님(<see cref="CouponCodes.TryNormalize"/>)</summary>
		public const int InvalidCode = 11001;

		/// <summary>캠페인이 아직 시작하지 않음</summary>
		public const int NotStarted = 11002;

		/// <summary>캠페인이 끝남</summary>
		public const int Expired = 11003;

		/// <summary>운영자가 캠페인을 껐음</summary>
		public const int Disabled = 11004;

		/// <summary>
		/// 이 계정이 이 캠페인을 이미 썼음. 서버 응답은 처음 지급 결과를 함께 담고, 클라이언트는 이것을 실패가 아니라 <c>AlreadyRedeemed</c> 가 true 인 성공으로 돌려줍니다
		/// </summary>
		public const int AlreadyRedeemed = 11005;

		/// <summary>개별 일회용 코드를 다른 계정이 이미 썼음</summary>
		public const int CodeUsed = 11006;

		/// <summary>캠페인 전체 사용 한도에 닿음</summary>
		public const int SoldOut = 11007;

		/// <summary>대상 조건(플랫폼, 국가, 최소 앱 버전)에 맞지 않음</summary>
		public const int NotEligible = 11008;

		/// <summary>게임 서버(지급자)가 지급을 거절함(계정 조건 등)</summary>
		public const int GrantRejected = 11009;

		/// <summary>게임 서버가 지금은 지급할 수 없다고 함. 나중에 다시 시도합니다</summary>
		public const int GrantDeferred = 11010;

		/// <summary>코드 실패가 너무 많아 잠시 막음(HTTP 429). <see cref="CouponRedeemResponse{TGrant}.RetryAfterSeconds"/> 뒤에 다시 시도합니다</summary>
		public const int TooManyAttempts = 11011;

		/// <summary>요청 형식이 잘못됨(HTTP 400·413·415)</summary>
		public const int BadRequest = 11012;

		/// <summary>계정을 확인하지 못함(HTTP 401). 로그인 뒤 다시 시도합니다</summary>
		public const int Unauthorized = 11013;

		/// <summary>
		/// 코드 때문에 실패했는지입니다. 서버는 이 실패를 계정별로 세어 너무 많으면 막습니다(무작위 대입 방지). 이미 쓴 캠페인, 게임 서버의 거절·보류, 잠금, 요청 오류는 세지 않습니다.
		/// </summary>
		public static bool IsCodeFailure(int code)
		{
			switch (code)
			{
				case InvalidCode:
				case NotStarted:
				case Expired:
				case Disabled:
				case CodeUsed:
				case SoldOut:
				case NotEligible:
					return true;
				default:
					return false;
			}
		}
	}
}
