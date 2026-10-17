using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Coupon
{
	/// <summary>
	/// 기본 쿠폰 사용입니다. 부트스트랩에서 만들어 <see cref="ICouponService"/> 로 바인딩합니다. 규칙은 <see cref="ICouponService"/> 를 봅니다.
	/// </summary>
	public sealed class CouponService : ICouponService
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Coupon");

		private readonly IHttpClient _http;
		private readonly string _route;
		private readonly string _platform;
		private readonly string _appVersion;
		private readonly string _country;
		private readonly TimeSpan _timeout;

		// 서버(System.Text.Json Web 기본값)와 같은 camelCase. 설정 객체는 바꿀 수 있으므로 서비스마다 따로 둔다
		private readonly JsonSerializerSettings _json = new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore,
			DateParseHandling = DateParseHandling.DateTimeOffset
		};

		/// <summary>메인 스레드에서 만듭니다(<c>Application.version</c> 을 읽음).</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">HTTP 클라이언트나 경로가 없거나 제한 시간이 0 이하인 경우</exception>
		public CouponService(CouponServiceOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.HttpClient == null || string.IsNullOrWhiteSpace(options.Route))
			{
				throw new ArgumentException("HttpClient and Route are required", nameof(options));
			}

			if (options.RequestTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentException("RequestTimeout must be positive", nameof(options));
			}

			_http = options.HttpClient;
			_route = options.Route;
			_platform = options.Platform ?? CouponPlatforms.Current;
			_appVersion = options.AppVersion ?? Application.version;
			_country = options.Country;
			_timeout = options.RequestTimeout;
		}

		public async UniTask<Result<CouponRedemption<TGrant>>> RedeemAsync<TGrant>(string code, CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result<CouponRedemption<TGrant>>.Fail(ResultCodes.Cancelled, "Coupon redeem was cancelled");
			}

			if (!CouponCodes.TryNormalize(code, out var normalized))
			{
				return Result<CouponRedemption<TGrant>>.Fail(CouponResultCodes.InvalidCode,
					$"Not a coupon code ({CouponCodes.MinLength}-{CouponCodes.MaxLength} letters and digits)");
			}

			var body = new CouponRedeemRequest { Code = normalized, Platform = _platform, AppVersion = _appVersion, Country = _country };
			var request = HttpRequest.Post(_route).WithJsonBody(body, _json).WithTimeout(_timeout);
			var sent = await _http.SendAsync(request, cancellationToken);

			if (!sent.TryGetValue(out var response))
			{
				return Result<CouponRedemption<TGrant>>.Fail(sent.Code, sent.Message);
			}

			// 서버가 만든 응답(200, 400, 401, 413, 415, 429)만 본문에 결과 코드가 있다. 그 밖(5xx, 프록시 오류)의 JSON(ProblemDetails 등)을 읽으면 code 가 없어 0(성공)이 된다
			if (!IsCouponStatus(response.StatusCode))
			{
				return Result<CouponRedemption<TGrant>>.Fail(HttpResultCodes.HttpError, $"HTTP {response.StatusCode} from {response.Uri}");
			}

			var read = response.ReadJson<CouponRedeemResponse<TGrant>>(_json);

			if (!read.TryGetValue(out var redeem) || redeem?.Code == null || redeem.Code.Value < 0)
			{
				return Unreadable<TGrant>(response);
			}

			var resultCode = redeem.Code.Value;

			if (resultCode == 0 || resultCode == CouponResultCodes.AlreadyRedeemed)
			{
				// 성공은 200 이고 서버가 늘 캠페인 ID 를 넣는다. 아니면 쿠폰 서버의 응답이 아니다(지급하지 않았을 수 있는데 성공으로 알리지 않게)
				if (response.StatusCode != 200 || string.IsNullOrWhiteSpace(redeem.CampaignId))
				{
					return Unreadable<TGrant>(response);
				}

				return Result<CouponRedemption<TGrant>>.Ok(
					new CouponRedemption<TGrant>(redeem.CampaignId, redeem.Grant, redeem.RedeemedAt, resultCode == CouponResultCodes.AlreadyRedeemed));
			}

			var message = redeem.RetryAfterSeconds.HasValue ? $"{redeem.Message} (retry after {redeem.RetryAfterSeconds.Value}s)" : redeem.Message;
			return Result<CouponRedemption<TGrant>>.Fail(resultCode, message);
		}

		/// <summary>
		/// 결과 코드를 읽지 못한 응답입니다. 앞단(인증 미들웨어, 요청 수 제한)이 본문 없이 답한 401·429·4xx 는 상태로 코드를 정하고, 그 밖은 <c>InvalidResponse</c> 입니다.
		/// </summary>
		private static Result<CouponRedemption<TGrant>> Unreadable<TGrant>(HttpResponse response)
		{
			switch (response.StatusCode)
			{
				case 401:
					return Result<CouponRedemption<TGrant>>.Fail(CouponResultCodes.Unauthorized, $"HTTP 401 from {response.Uri}");
				case 429:
					return Result<CouponRedemption<TGrant>>.Fail(CouponResultCodes.TooManyAttempts, $"HTTP 429 from {response.Uri}");
				case 400:
				case 413:
				case 415:
					return Result<CouponRedemption<TGrant>>.Fail(CouponResultCodes.BadRequest, $"HTTP {response.StatusCode} from {response.Uri}");
				default:
					Logger.Warning($"Unreadable coupon response, HTTP {response.StatusCode} from {response.Uri}");
					return Result<CouponRedemption<TGrant>>.Fail(HttpResultCodes.InvalidResponse, $"Unreadable coupon response from {response.Uri}");
			}
		}

		private static bool IsCouponStatus(int status)
		{
			return status == 200 || status == 400 || status == 401 || status == 413 || status == 415 || status == 429;
		}
	}
}
