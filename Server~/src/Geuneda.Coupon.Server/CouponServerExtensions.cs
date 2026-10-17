using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geuneda.Coupon.Server
{
	/// <summary>쿠폰 서버 등록 확장입니다.</summary>
	public static class CouponServerExtensions
	{
		/// <summary>
		/// 사용 처리(<see cref="ICouponRedeemer"/>, 요청 범위), 운영 입구(<see cref="CouponAdmin"/>, 요청 범위), 기본 실패 기록(메모리)과 설정을 등록합니다.
		/// 돌려준 빌더로 저장소와 지급자를 붙입니다. 설정이 잘못되면 시작할 때 실패합니다.
		/// </summary>
		public static CouponServerBuilder AddGeunedaCoupon(this IServiceCollection services, Action<CouponServerOptions> configure = null)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			var options = services.AddOptions<CouponServerOptions>();

			if (configure != null)
			{
				options.Configure(configure);
			}

			options.Validate(o => o.MaxFailures >= 1 && o.FailureWindow > TimeSpan.Zero && o.MaxRequestBytes >= 256,
					"CouponServerOptions is invalid (MaxFailures < 1, FailureWindow <= 0 or MaxRequestBytes < 256)")
				.ValidateOnStart();
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<ICouponAttemptStore, InMemoryCouponAttemptStore>();
			services.TryAddScoped<ICouponRedeemer, CouponRedeemer>();
			services.TryAddScoped<CouponAdmin>();
			return new CouponServerBuilder(services);
		}

		/// <summary>
		/// <c>POST {prefix}/redeem</c> 사용 엔드포인트를 붙입니다. 요청 수 제한 등은 돌려준 빌더에 붙입니다. 저장소·지급자가 없으면 여기서 실패합니다.
		/// </summary>
		/// <param name="resolveAccountId">
		/// 요청의 계정 ID 를 돌려줍니다(account 패키지면 <c>IAccessTokenValidator</c> 로 Bearer 토큰 검증). 인증하지 못하면 null 을 돌려주고, 엔드포인트가 401 로 답합니다
		/// </param>
		public static RouteHandlerBuilder MapGeunedaCoupon(this IEndpointRouteBuilder endpoints, string prefix, Func<HttpContext, Task<string>> resolveAccountId)
		{
			if (endpoints == null)
			{
				throw new ArgumentNullException(nameof(endpoints));
			}

			if (prefix == null)
			{
				throw new ArgumentNullException(nameof(prefix));
			}

			if (resolveAccountId == null)
			{
				throw new ArgumentNullException(nameof(resolveAccountId));
			}

			var registry = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

			if (!registry.IsService(typeof(ICouponRedeemer)))
			{
				throw new InvalidOperationException("Call AddGeunedaCoupon before MapGeunedaCoupon");
			}

			if (!registry.IsService(typeof(ICouponStore)))
			{
				throw new InvalidOperationException("Register a coupon store (AddStore or AddInMemoryStore) before MapGeunedaCoupon");
			}

			if (!registry.IsService(typeof(ICouponGrantor)))
			{
				throw new InvalidOperationException("Register a coupon grantor (AddGrantor) before MapGeunedaCoupon");
			}

			Func<HttpContext, Task<IResult>> handler = context => CouponRedeemEndpoint.HandleAsync(context, resolveAccountId);
			return endpoints.MapPost(prefix.TrimEnd('/') + "/" + CouponRoutes.Redeem, handler);
		}
	}
}
