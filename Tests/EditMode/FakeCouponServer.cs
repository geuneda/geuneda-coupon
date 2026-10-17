using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon.Tests
{
	/// <summary>대본대로 답하는 가짜 HTTP 전송입니다. 요청을 모으고, 요청이 동기적으로 끝납니다.</summary>
	internal sealed class FakeCouponServer : IHttpTransport
	{
		public const string BaseAddress = "https://game.test/coupons/";

		public readonly List<HttpRequest> Requests = new List<HttpRequest>();

		public int StatusCode = 200;
		public string Body = "{}";

		/// <summary>있으면 응답 대신 이 결과를 돌려줍니다(연결 실패 등).</summary>
		public Result<HttpResponse>? Failure;

		public HttpClient CreateClient()
		{
			return new HttpClientBuilder().WithBaseAddress(BaseAddress).WithTransport(this).Build();
		}

		public string LastBody => Encoding.UTF8.GetString(Requests[Requests.Count - 1].Body);

		public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);

			if (Failure.HasValue)
			{
				return UniTask.FromResult(Failure.Value);
			}

			return UniTask.FromResult(Result<HttpResponse>.Ok(new HttpResponse(request.Uri, StatusCode, Encoding.UTF8.GetBytes(Body), null)));
		}
	}
}
