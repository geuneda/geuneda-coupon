namespace Geuneda.Coupon.Server
{
	/// <summary>이름 규칙입니다. 런타임 문자 표에 기대지 않도록 ASCII 만 받고 ASCII 로만 대소문자를 바꿉니다.</summary>
	internal static class CouponNames
	{
		public const int MaxIdLength = 128;
		public const int MaxPlatformLength = 32;
		public const int MaxAppVersionLength = 64;

		/// <summary>캠페인 ID: 1~128자, 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>.</summary>
		public static bool IsId(string value)
		{
			if (string.IsNullOrEmpty(value) || value.Length > MaxIdLength)
			{
				return false;
			}

			foreach (var c in value)
			{
				if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>플랫폼 이름: 1~32자, 영문 소문자·숫자·<c>_</c>·<c>-</c>.</summary>
		public static bool IsPlatform(string value)
		{
			if (string.IsNullOrEmpty(value) || value.Length > MaxPlatformLength)
			{
				return false;
			}

			foreach (var c in value)
			{
				if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>클라이언트가 보낸 플랫폼 이름을 소문자로 바꿉니다. 규칙에 맞지 않으면 null 입니다.</summary>
		public static string NormalizePlatform(string value)
		{
			if (value == null)
			{
				return null;
			}

			var chars = value.Trim().ToCharArray();

			for (var i = 0; i < chars.Length; i++)
			{
				if (chars[i] >= 'A' && chars[i] <= 'Z')
				{
					chars[i] = (char) (chars[i] + ('a' - 'A'));
				}
			}

			var normalized = new string(chars);
			return IsPlatform(normalized) ? normalized : null;
		}

		/// <summary>ISO 3166-1 두 글자 국가 코드를 대문자로 바꿉니다. 영문 두 글자가 아니면 null 입니다.</summary>
		public static string NormalizeCountry(string value)
		{
			if (value == null)
			{
				return null;
			}

			var trimmed = value.Trim();

			if (trimmed.Length != 2)
			{
				return null;
			}

			var chars = new char[2];

			for (var i = 0; i < 2; i++)
			{
				var c = trimmed[i];

				if (c >= 'a' && c <= 'z')
				{
					c = (char) (c - ('a' - 'A'));
				}

				if (c < 'A' || c > 'Z')
				{
					return null;
				}

				chars[i] = c;
			}

			return new string(chars);
		}

		/// <summary>앱 버전 문자열을 다듬습니다. 비었거나 64자보다 길면 null 입니다.</summary>
		public static string NormalizeAppVersion(string value)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxAppVersionLength ? null : trimmed;
		}
	}
}
