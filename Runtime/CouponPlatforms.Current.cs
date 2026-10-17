// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	public static partial class CouponPlatforms
	{
		/// <summary>
		/// 빌드 대상의 플랫폼 이름입니다. 에디터에서는 활성 빌드 대상을 따릅니다(Android 대상이면 <see cref="Android"/>).
		/// </summary>
		public static string Current
		{
			get
			{
#if UNITY_ANDROID
				return Android;
#elif UNITY_IOS
				return Ios;
#elif UNITY_WEBGL
				return WebGl;
#else
				return Standalone;
#endif
			}
		}
	}
}
