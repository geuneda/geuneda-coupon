using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon.Tests
{
	/// <summary>
	/// 실제 .NET 샘플 쿠폰 서버(<c>Server~/samples/Geuneda.Coupon.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 새로 띄우고(메모리 저장소라 개별 코드는 한 번만 쓸 수 있음) <c>GEUNEDA_COUPON_INTEROP_URL</c>(예: <c>http://127.0.0.1:5699/coupons/</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 쿠폰 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_COUPON_INTEROP_URL";

		public sealed class SampleGrant
		{
			public SampleItem[] Items { get; set; }
			public int Gems { get; set; }
		}

		public sealed class SampleItem
		{
			public string Id { get; set; }
			public int Amount { get; set; }
		}

		private static string ServerUrl()
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);

			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			return url.TrimEnd('/') + "/";
		}

		private static CouponService Service(string account)
		{
			var http = new HttpClientBuilder()
				.WithBaseAddress(ServerUrl())
				.AddMiddleware(new DefaultHeadersMiddleware(new Dictionary<string, string> { ["X-Sample-Account"] = account }))
				.Build();
			return new CouponService(new CouponServiceOptions { HttpClient = http, Platform = CouponPlatforms.Android, AppVersion = "1.0.0" });
		}

		[UnityTest]
		public IEnumerator SampleServer_RedeemsOnce_ReportsFailures_AndLocks() => UniTask.ToCoroutine(async () =>
		{
			var alice = Service("alice-" + Guid.NewGuid().ToString("N"));
			var bob = Service("bob-" + Guid.NewGuid().ToString("N"));

			var first = await alice.RedeemAsync<SampleGrant>("welcome-2026");
			Assert.IsTrue(first.IsSuccess, first.ToString());
			Assert.IsFalse(first.Value.AlreadyRedeemed);
			Assert.AreEqual("welcome", first.Value.CampaignId);
			Assert.AreEqual(100, first.Value.Grant.Items[0].Amount);
			Assert.AreEqual(100, first.Value.Grant.Gems);
			Assert.IsTrue(first.Value.RedeemedAt.HasValue);

			var again = await alice.RedeemAsync<SampleGrant>("WELCOME2026");
			Assert.IsTrue(again.IsSuccess, again.ToString());
			Assert.IsTrue(again.Value.AlreadyRedeemed);
			Assert.AreEqual(100, again.Value.Grant.Gems, "The first grant comes back, not a second one");

			var unique = await alice.RedeemAsync<SampleGrant>("launch-0001");
			Assert.IsTrue(unique.IsSuccess, unique.ToString());
			Assert.AreEqual(150, unique.Value.Grant.Gems);

			Assert.AreEqual(CouponResultCodes.CodeUsed, (await bob.RedeemAsync<SampleGrant>("LAUNCH0001")).Code);
			Assert.AreEqual(CouponResultCodes.Expired, (await bob.RedeemAsync<SampleGrant>("OLD2020")).Code);
			Assert.AreEqual(CouponResultCodes.GrantRejected, (await bob.RedeemAsync<SampleGrant>("ZERO2026")).Code);

			// 샘플 서버는 실패 5번에서 막는다. bob 은 사용됨·끝남 2번을 냈으므로 3번 더
			for (var i = 0; i < 3; i++)
			{
				Assert.AreEqual(CouponResultCodes.InvalidCode, (await bob.RedeemAsync<SampleGrant>("WRONG000" + i)).Code);
			}

			var locked = await bob.RedeemAsync<SampleGrant>("WELCOME2026");
			Assert.AreEqual(CouponResultCodes.TooManyAttempts, locked.Code, locked.ToString());
			StringAssert.Contains("retry after", locked.Message);

			var anonymous = new CouponService(new CouponServiceOptions { HttpClient = new HttpClientBuilder().WithBaseAddress(ServerUrl()).Build() });
			Assert.AreEqual(CouponResultCodes.Unauthorized, (await anonymous.RedeemAsync<SampleGrant>("WELCOME2026")).Code);
		});
	}
}
