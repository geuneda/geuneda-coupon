using System;
using System.Threading.Tasks;
using Geuneda.Coupon.SampleServer;
using Geuneda.Coupon.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// 실행: dotnet run --project "Server~/samples/Geuneda.Coupon.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5699
// Unity 상호 운용 테스트: GEUNEDA_COUPON_INTEROP_URL=http://127.0.0.1:5699/coupons/
// 계정은 X-Sample-Account 헤더로 받는다(샘플 전용). 운영에서는 account 패키지의 IAccessTokenValidator 로 Bearer 토큰을 검증한다(README).
// 샘플 캠페인: 공용 WELCOME2026(젬 100), 개별 코드 LAUNCH-0001~0003(젬 50, 캠페인당 계정 하나), 끝난 OLD2020, 거절되는 ZERO2026(보상 0)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeunedaCoupon(options => options.MaxFailures = 5)
	.AddInMemoryStore()
	.AddGrantor<SampleWallet>(ServiceLifetime.Singleton);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var admin = scope.ServiceProvider.GetRequiredService<CouponAdmin>();
	await admin.AddCampaignAsync(new CouponCampaign("welcome", CouponKind.Shared, "WELCOME2026", "100"));
	await admin.AddCampaignAsync(new CouponCampaign("launch", CouponKind.Unique, reward: "50"));
	await admin.AddCodesAsync("launch", new[] { "LAUNCH-0001", "LAUNCH-0002", "LAUNCH-0003" });
	await admin.AddCampaignAsync(new CouponCampaign("old", CouponKind.Shared, "OLD2020", "10", endsAt: new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)));
	await admin.AddCampaignAsync(new CouponCampaign("zero", CouponKind.Shared, "ZERO2026", "0"));
}

app.MapGeunedaCoupon("/coupons", context =>
{
	var account = context.Request.Headers["X-Sample-Account"].ToString();
	return Task.FromResult(string.IsNullOrEmpty(account) ? null : account);
});
app.Run();
