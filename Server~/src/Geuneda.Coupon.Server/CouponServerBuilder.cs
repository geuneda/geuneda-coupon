using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geuneda.Coupon.Server
{
	/// <summary><see cref="CouponServerExtensions.AddGeunedaCoupon"/> 가 돌려주는 등록 빌더입니다. 저장소, 지급자, 실패 기록, 맥락 해석기를 붙입니다.</summary>
	public sealed class CouponServerBuilder
	{
		internal CouponServerBuilder(IServiceCollection services)
		{
			Services = services;
		}

		public IServiceCollection Services { get; }

		/// <summary>저장소를 붙입니다. 지급자와 같은 DB 트랜잭션을 쓰도록 기본 수명은 요청 범위입니다.</summary>
		public CouponServerBuilder AddStore<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where T : class, ICouponStore
		{
			Services.Replace(ServiceDescriptor.Describe(typeof(ICouponStore), typeof(T), lifetime));
			return this;
		}

		/// <summary>메모리 저장소(<see cref="InMemoryCouponStore"/>, 싱글턴)를 붙입니다. 개발·테스트용입니다.</summary>
		public CouponServerBuilder AddInMemoryStore()
		{
			Services.TryAddSingleton<InMemoryCouponStore>();
			Services.Replace(ServiceDescriptor.Singleton<ICouponStore>(provider => provider.GetRequiredService<InMemoryCouponStore>()));
			return this;
		}

		/// <summary>게임 지급자를 붙입니다. 저장소와 같은 DB 트랜잭션을 쓰도록 기본 수명은 요청 범위입니다.</summary>
		public CouponServerBuilder AddGrantor<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where T : class, ICouponGrantor
		{
			Services.Replace(ServiceDescriptor.Describe(typeof(ICouponGrantor), typeof(T), lifetime));
			return this;
		}

		/// <summary>실패 기록을 바꿉니다(기본 <see cref="InMemoryCouponAttemptStore"/>). 서버가 여러 대면 공유 저장소로 구현해 붙입니다.</summary>
		public CouponServerBuilder AddAttemptStore<T>(ServiceLifetime lifetime = ServiceLifetime.Singleton) where T : class, ICouponAttemptStore
		{
			Services.Replace(ServiceDescriptor.Describe(typeof(ICouponAttemptStore), typeof(T), lifetime));
			return this;
		}

		/// <summary>대상 조건 맥락을 서버 값으로 바꾸는 해석기를 붙입니다(선택). 기본 수명은 요청 범위입니다.</summary>
		public CouponServerBuilder AddContextResolver<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where T : class, ICouponContextResolver
		{
			Services.Replace(ServiceDescriptor.Describe(typeof(ICouponContextResolver), typeof(T), lifetime));
			return this;
		}
	}
}
