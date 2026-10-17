namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 대상 조건을 판정할 요청의 맥락입니다. 계정 ID 는 인증에서, 나머지는 클라이언트가 보낸 값(위조할 수 있음)을 다듬은 것이고,
	/// <see cref="ICouponContextResolver"/> 가 서버 값(CDN 국가 헤더, 계정 DB 의 플랫폼 등)으로 바꿀 수 있습니다.
	/// </summary>
	public sealed class CouponContext
	{
		/// <param name="accountId">인증한 계정 ID. 비어 있으면 안 됩니다</param>
		/// <param name="platform">플랫폼 이름. 소문자로 바꾸고 규칙(1~32자 영문 소문자·숫자·<c>_</c>·<c>-</c>)에 맞지 않으면 null</param>
		/// <param name="appVersion">앱 버전. 비었거나 64자보다 길면 null</param>
		/// <param name="country">ISO 3166-1 두 글자 국가 코드. 대문자로 바꾸고 영문 두 글자가 아니면 null</param>
		/// <exception cref="System.ArgumentException">계정 ID 가 비어 있는 경우</exception>
		public CouponContext(string accountId, string platform, string appVersion, string country)
		{
			if (string.IsNullOrEmpty(accountId))
			{
				throw new System.ArgumentException("Account id is required", nameof(accountId));
			}

			AccountId = accountId;
			Platform = CouponNames.NormalizePlatform(platform);
			AppVersion = CouponNames.NormalizeAppVersion(appVersion);
			Country = CouponNames.NormalizeCountry(country);
		}

		public string AccountId { get; }

		public string Platform { get; }

		public string AppVersion { get; }

		public string Country { get; }

		public override string ToString()
		{
			return $"account {AccountId}, platform {Platform ?? "-"}, version {AppVersion ?? "-"}, country {Country ?? "-"}";
		}
	}
}
