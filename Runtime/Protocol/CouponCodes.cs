// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>쿠폰 코드 모양입니다. 클라이언트(보내기 전 확인)와 서버(찾기, 등록)가 같은 규칙을 씁니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.</summary>
	public static class CouponCodes
	{
		public const int MinLength = 4;
		public const int MaxLength = 64;

		/// <summary>
		/// 입력을 비교용 코드로 바꿉니다: 공백(스페이스, 탭)과 <c>-</c> 를 빼고 영문을 대문자로 바꾼 뒤, 영문 대문자·숫자만 남은 4~64자여야 합니다(<c>abcd-efgh</c> → <c>ABCDEFGH</c>).
		/// 런타임 문자 표에 기대지 않도록 ASCII 만 받습니다. 맞지 않으면 false 와 null 입니다.
		/// </summary>
		public static bool TryNormalize(string raw, out string normalized)
		{
			normalized = null;

			if (raw == null)
			{
				return false;
			}

			var chars = new char[raw.Length];
			var length = 0;

			foreach (var c in raw)
			{
				if (c == ' ' || c == '\t' || c == '-')
				{
					continue;
				}

				if (c >= 'a' && c <= 'z')
				{
					chars[length++] = (char) (c - ('a' - 'A'));
				}
				else if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
				{
					chars[length++] = c;
				}
				else
				{
					return false;
				}
			}

			if (length < MinLength || length > MaxLength)
			{
				return false;
			}

			normalized = new string(chars, 0, length);
			return true;
		}
	}
}
