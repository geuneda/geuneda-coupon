using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 대상 조건 판정 맥락을 서버 값으로 바꿉니다(선택, <see cref="CouponServerBuilder.AddContextResolver{T}"/>). 예: CDN 의 국가 헤더(<c>CF-IPCountry</c>)로 국가를,
	/// 계정 DB 의 가입 플랫폼으로 플랫폼을 정합니다. 없으면 클라이언트가 보낸 값(위조 가능)을 씁니다.
	/// </summary>
	public interface ICouponContextResolver
	{
		/// <param name="reported">클라이언트가 보낸 값을 다듬은 맥락(계정 ID 는 인증 값)</param>
		/// <returns>판정에 쓸 맥락. 계정 ID 는 바꾸지 않습니다(바꾸면 예외)</returns>
		Task<CouponContext> ResolveAsync(HttpContext http, CouponContext reported, CancellationToken cancellationToken);
	}
}
