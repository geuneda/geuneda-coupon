using System;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary><see cref="CouponService"/> 설정입니다.</summary>
	public sealed class CouponServiceOptions
	{
		/// <summary>
		/// 쿠폰 서버 접두사까지 담은 기본 주소(예: <c>https://game.example.com/coupons/</c>)의 HTTP 클라이언트입니다. 필수입니다.
		/// 계정 인증은 미들웨어(account 패키지의 <c>AuthTokenMiddleware</c>)가 붙입니다. 재시도 미들웨어는 POST 를 다시 보내지 않습니다.
		/// </summary>
		public IHttpClient HttpClient { get; set; }

		/// <summary>사용 경로입니다. 기본 <see cref="CouponRoutes.Redeem"/>.</summary>
		public string Route { get; set; } = CouponRoutes.Redeem;

		/// <summary>대상 조건에 보낼 플랫폼 이름입니다. null 이면 <see cref="CouponPlatforms.Current"/> 입니다.</summary>
		public string Platform { get; set; }

		/// <summary>대상 조건에 보낼 앱 버전입니다. null 이면 <c>Application.version</c> 입니다.</summary>
		public string AppVersion { get; set; }

		/// <summary>
		/// 대상 조건에 보낼 ISO 3166-1 두 글자 국가 코드입니다. 기본 null(보내지 않음). 기기 지역은 플랫폼마다 믿기 어려워 게임이 정해 넣거나, 서버가 맥락 해석기(CDN 국가 헤더 등)로 정합니다.
		/// </summary>
		public string Country { get; set; }

		/// <summary>요청 하나의 제한 시간입니다. 넘으면 <c>ResultCodes.Timeout</c> 입니다(서버는 지급했을 수 있음). 기본 10초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
	}
}
