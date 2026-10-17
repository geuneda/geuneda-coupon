using System;
using System.Collections.Generic;

namespace Geuneda.Coupon.Server
{
	/// <summary>캠페인의 대상 조건입니다. 모든 조건을 통과해야 씁니다. 만든 뒤 바뀌지 않습니다.</summary>
	public sealed class CouponConditions
	{
		// None 보다 먼저 초기화해야 한다(정적 필드는 선언 순서로 초기화되고 None 의 생성자가 이것을 씀)
		private static readonly IReadOnlyList<string> Any = Array.Empty<string>();

		/// <summary>조건이 없습니다(모두 씀).</summary>
		public static readonly CouponConditions None = new CouponConditions();

		private readonly int[] _minAppVersion;

		/// <param name="platforms">쓸 수 있는 플랫폼 이름(<see cref="CouponPlatforms"/> 등). null 이면 모두. 빈 목록은 받지 않습니다</param>
		/// <param name="countries">쓸 수 있는 ISO 3166-1 두 글자 국가 코드(대소문자 무시). null 이면 모두. 빈 목록은 받지 않습니다. 국가를 모르는 요청은 막힙니다</param>
		/// <param name="minAppVersion">최소 앱 버전(<c>1.2.0</c>, 숫자 1~4 부분). null 이면 없음. 숫자로 읽히지 않는 앱 버전의 요청은 막힙니다</param>
		/// <exception cref="ArgumentException">이름·코드·버전이 규칙에 맞지 않거나, 빈 목록이거나, 겹치는 경우</exception>
		public CouponConditions(IEnumerable<string> platforms = null, IEnumerable<string> countries = null, string minAppVersion = null)
		{
			Platforms = Copy(platforms, nameof(platforms), value => CouponNames.IsPlatform(value) ? value : null);
			Countries = Copy(countries, nameof(countries), CouponNames.NormalizeCountry);

			if (minAppVersion != null && !CouponVersion.TryParse(minAppVersion, out _minAppVersion))
			{
				throw new ArgumentException($"Minimum app version must be 1-4 numeric parts such as 1.2.0: '{minAppVersion}'", nameof(minAppVersion));
			}

			MinAppVersion = minAppVersion;
		}

		/// <summary>쓸 수 있는 플랫폼입니다. 비어 있으면 모두입니다.</summary>
		public IReadOnlyList<string> Platforms { get; }

		/// <summary>쓸 수 있는 국가(대문자)입니다. 비어 있으면 모두입니다.</summary>
		public IReadOnlyList<string> Countries { get; }

		/// <summary>최소 앱 버전입니다. 없으면 null 입니다.</summary>
		public string MinAppVersion { get; }

		/// <summary>이 맥락이 모든 조건을 통과하는지입니다.</summary>
		public bool Allows(CouponContext context)
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			if (Platforms.Count > 0 && !Contains(Platforms, context.Platform))
			{
				return false;
			}

			if (Countries.Count > 0 && !Contains(Countries, context.Country))
			{
				return false;
			}

			if (_minAppVersion != null)
			{
				return CouponVersion.TryParse(context.AppVersion, out var version) && CouponVersion.Compare(version, _minAppVersion) >= 0;
			}

			return true;
		}

		private static bool Contains(IReadOnlyList<string> values, string value)
		{
			if (value == null)
			{
				return false;
			}

			foreach (var item in values)
			{
				if (string.Equals(item, value, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		private static IReadOnlyList<string> Copy(IEnumerable<string> values, string name, Func<string, string> normalize)
		{
			if (values == null)
			{
				return Any;
			}

			var copied = new List<string>();

			foreach (var value in values)
			{
				var normalized = normalize(value) ?? throw new ArgumentException($"Invalid value '{value}'", name);

				if (copied.Contains(normalized))
				{
					throw new ArgumentException($"'{value}' appears twice", name);
				}

				copied.Add(normalized);
			}

			if (copied.Count == 0)
			{
				throw new ArgumentException("The list is empty (pass null to allow every value)", name);
			}

			return copied.AsReadOnly();
		}
	}
}
