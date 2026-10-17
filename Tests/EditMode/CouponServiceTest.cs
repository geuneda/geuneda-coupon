using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon.Tests
{
	[TestFixture]
	public class CouponServiceTest
	{
		private FakeCouponServer _server;

		public sealed class TestGrant
		{
			public TestItem[] Items { get; set; }
			public int Gems { get; set; }
		}

		public sealed class TestItem
		{
			public string Id { get; set; }
			public int Amount { get; set; }
		}

		[SetUp]
		public void SetUp()
		{
			_server = new FakeCouponServer();
		}

		private CouponService CreateService(Action<CouponServiceOptions> change = null)
		{
			var options = new CouponServiceOptions
			{
				HttpClient = _server.CreateClient(),
				Platform = CouponPlatforms.Ios,
				AppVersion = "1.2.3",
				Country = "KR",
				RequestTimeout = TimeSpan.FromSeconds(3)
			};
			change?.Invoke(options);
			return new CouponService(options);
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		private Result<CouponRedemption<TestGrant>> Redeem(string code = "WELCOME2026", CouponService service = null)
		{
			return Run((service ?? CreateService()).RedeemAsync<TestGrant>(code));
		}

		private void Respond(int status, string body)
		{
			_server.StatusCode = status;
			_server.Body = body;
		}

		private const string Granted =
			"{\"code\":0,\"campaignId\":\"welcome\",\"grant\":{\"items\":[{\"id\":\"gem\",\"amount\":100}],\"gems\":150},\"redeemedAt\":\"2026-10-17T03:00:00+00:00\"}";

		[Test]
		public void Constructor_InvalidOptions_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new CouponService(null));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.HttpClient = null));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.Route = " "));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.RequestTimeout = TimeSpan.Zero));
		}

		[Test]
		public void Redeem_SendsNormalizedCodeAndContext()
		{
			Respond(200, Granted);

			Redeem(" welcome-2026 ");

			var request = _server.Requests[0];
			Assert.AreEqual(HttpMethod.Post, request.Method);
			Assert.AreEqual(FakeCouponServer.BaseAddress + CouponRoutes.Redeem, request.Uri.AbsoluteUri);
			Assert.AreEqual(TimeSpan.FromSeconds(3), request.Timeout);
			StringAssert.StartsWith("application/json", request.ContentType);

			var body = JObject.Parse(_server.LastBody);
			Assert.AreEqual("WELCOME2026", (string) body["code"]);
			Assert.AreEqual("ios", (string) body["platform"]);
			Assert.AreEqual("1.2.3", (string) body["appVersion"]);
			Assert.AreEqual("KR", (string) body["country"]);
		}

		[Test]
		public void Redeem_Defaults_UseBuildPlatformAndApplicationVersion()
		{
			Respond(200, Granted);
			var service = CreateService(o =>
			{
				o.Platform = null;
				o.AppVersion = null;
				o.Country = null;
			});

			Redeem(service: service);

			var body = JObject.Parse(_server.LastBody);
			Assert.AreEqual(CouponPlatforms.Current, (string) body["platform"]);
			Assert.AreEqual(Application.version, (string) body["appVersion"]);
			Assert.IsNull(body["country"], "Null values are not sent");
		}

		[Test]
		public void Redeem_MalformedCode_FailsWithoutRequest()
		{
			foreach (var code in new[] { null, "", "abc", "WELCOME_2026", "쿠폰코드입니다" })
			{
				Assert.AreEqual(CouponResultCodes.InvalidCode, Redeem(code).Code, code);
			}

			Assert.AreEqual(0, _server.Requests.Count);
		}

		[Test]
		public void Redeem_Granted_ReturnsTypedGrant()
		{
			Respond(200, Granted);

			var result = Redeem();

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual("welcome", result.Value.CampaignId);
			Assert.IsFalse(result.Value.AlreadyRedeemed);
			Assert.AreEqual(150, result.Value.Grant.Gems);
			Assert.AreEqual("gem", result.Value.Grant.Items[0].Id);
			Assert.AreEqual(100, result.Value.Grant.Items[0].Amount);
			Assert.AreEqual(new DateTimeOffset(2026, 10, 17, 3, 0, 0, TimeSpan.Zero), result.Value.RedeemedAt);
		}

		[Test]
		public void Redeem_AlreadyRedeemed_IsSuccessWithTheFirstGrant()
		{
			Respond(200, Granted.Replace("\"code\":0", "\"code\":" + CouponResultCodes.AlreadyRedeemed));

			var result = Redeem();

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.IsTrue(result.Value.AlreadyRedeemed);
			Assert.AreEqual(150, result.Value.Grant.Gems);
		}

		[Test]
		public void Redeem_GrantMissing_IsSuccessWithDefault()
		{
			Respond(200, "{\"code\":0,\"campaignId\":\"welcome\",\"redeemedAt\":\"2026-10-17T03:00:00Z\"}");

			var result = Redeem();

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.IsNull(result.Value.Grant);
		}

		[Test]
		public void Redeem_ServerCodes_AreFailures()
		{
			foreach (var code in new[]
			{
				CouponResultCodes.InvalidCode, CouponResultCodes.NotStarted, CouponResultCodes.Expired, CouponResultCodes.Disabled, CouponResultCodes.CodeUsed,
				CouponResultCodes.SoldOut, CouponResultCodes.NotEligible, CouponResultCodes.GrantRejected, CouponResultCodes.GrantDeferred
			})
			{
				Respond(200, "{\"code\":" + code + ",\"message\":\"why\"}");
				var result = Redeem();
				Assert.AreEqual(code, result.Code);
				Assert.AreEqual("why", result.Message);
			}

			Respond(429, "{\"code\":" + CouponResultCodes.TooManyAttempts + ",\"message\":\"Too many\",\"retryAfterSeconds\":42}");
			var locked = Redeem();
			Assert.AreEqual(CouponResultCodes.TooManyAttempts, locked.Code);
			StringAssert.Contains("retry after 42s", locked.Message);

			Respond(401, "{\"code\":" + CouponResultCodes.Unauthorized + "}");
			Assert.AreEqual(CouponResultCodes.Unauthorized, Redeem().Code);
		}

		[Test]
		public void Redeem_BodylessFrontEndStatuses_MapByStatus()
		{
			Respond(401, "");
			Assert.AreEqual(CouponResultCodes.Unauthorized, Redeem().Code);

			Respond(429, "");
			Assert.AreEqual(CouponResultCodes.TooManyAttempts, Redeem().Code);

			Respond(413, "<html>too large</html>");
			Assert.AreEqual(CouponResultCodes.BadRequest, Redeem().Code);
		}

		[Test]
		public void Redeem_OtherStatuses_AreHttpErrors_EvenWithJson()
		{
			// ProblemDetails 처럼 code 가 없는 JSON 을 0(성공)으로 읽지 않는다
			Respond(500, "{\"type\":\"about:blank\",\"title\":\"Internal\",\"status\":500}");
			Assert.AreEqual(HttpResultCodes.HttpError, Redeem().Code);

			Respond(502, "<html>bad gateway</html>");
			Assert.AreEqual(HttpResultCodes.HttpError, Redeem().Code);

			Respond(404, "");
			Assert.AreEqual(HttpResultCodes.HttpError, Redeem().Code);
		}

		[Test]
		public void Redeem_UnreadableOrNonCouponSuccess_IsInvalidResponse()
		{
			foreach (var body in new[]
			{
				"{}", "{\"code\":0}", "<html>portal</html>", "", "{\"code\":-1}", "null", "{\"campaignId\":\"welcome\"}",
				"{\"code\":null,\"campaignId\":\"welcome\"}", "{\"code\":0,\"campaignId\":\"\"}", "{\"code\":0,\"campaignId\":\" \"}"
			})
			{
				Respond(200, body);
				Assert.AreEqual(HttpResultCodes.InvalidResponse, Redeem().Code, body);
			}

			Respond(400, "{\"type\":\"about:blank\",\"status\":400}");
			Assert.AreEqual(CouponResultCodes.BadRequest, Redeem().Code, "A 400 without a result code maps by status");

			Respond(401, Granted);
			Assert.AreEqual(CouponResultCodes.Unauthorized, Redeem().Code, "Success is only accepted with HTTP 200");
		}

		[Test]
		public void Redeem_TransportFailures_PassThrough()
		{
			_server.Failure = Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");
			Assert.AreEqual(ResultCodes.NetworkError, Redeem().Code);

			_server.Failure = Result<HttpResponse>.Fail(ResultCodes.Timeout, "slow");
			Assert.AreEqual(ResultCodes.Timeout, Redeem().Code);

			using (var cancelled = new CancellationTokenSource())
			{
				cancelled.Cancel();
				_server.Failure = null;
				Assert.AreEqual(ResultCodes.Cancelled, Run(CreateService().RedeemAsync<TestGrant>("WELCOME2026", cancelled.Token)).Code);
			}
		}

		[Test]
		public void Redeem_GrantAsJToken_KeepsRawJson()
		{
			Respond(200, Granted);

			var result = Run(CreateService().RedeemAsync<JObject>("WELCOME2026"));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(150, (int) result.Value.Grant["gems"]);
		}
	}
}
