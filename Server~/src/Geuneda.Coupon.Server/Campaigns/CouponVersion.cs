using System.Globalization;

namespace Geuneda.Coupon.Server
{
	/// <summary>최소 앱 버전 비교용 숫자 버전입니다(<c>1.10.2</c>, 1~4 부분, 빠진 부분은 0).</summary>
	internal static class CouponVersion
	{
		public static bool TryParse(string value, out int[] parts)
		{
			parts = null;

			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			var pieces = value.Split('.');

			if (pieces.Length < 1 || pieces.Length > 4)
			{
				return false;
			}

			var parsed = new int[4];

			for (var i = 0; i < pieces.Length; i++)
			{
				var piece = pieces[i];

				if (piece.Length == 0 || piece.Length > 9)
				{
					return false;
				}

				foreach (var c in piece)
				{
					if (c < '0' || c > '9')
					{
						return false;
					}
				}

				parsed[i] = int.Parse(piece, NumberStyles.None, CultureInfo.InvariantCulture);
			}

			parts = parsed;
			return true;
		}

		public static int Compare(int[] x, int[] y)
		{
			for (var i = 0; i < 4; i++)
			{
				var compared = x[i].CompareTo(y[i]);

				if (compared != 0)
				{
					return compared;
				}
			}

			return 0;
		}
	}
}
