using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// <c>POST {prefix}/redeem</c> 처리기입니다. 401(계정 확인 실패), 415(JSON 아님), 413(본문 초과), 400(형식 오류), 429(실패가 많아 잠김, <c>Retry-After</c>),
	/// 200(그 밖의 모든 결과, 본문의 <c>code</c> 로 구분)을 돌려줍니다. 모두 <see cref="CouponRedeemResponse{TGrant}"/> 본문입니다. 지급자·저장소 예외는 잡지 않아 500 입니다.
	/// </summary>
	internal static class CouponRedeemEndpoint
	{
		public static async Task<IResult> HandleAsync(HttpContext context, Func<HttpContext, Task<string>> resolveAccountId)
		{
			var accountId = await resolveAccountId(context);

			if (string.IsNullOrEmpty(accountId))
			{
				return Error(StatusCodes.Status401Unauthorized, CouponResultCodes.Unauthorized, "Account is not authenticated");
			}

			if (!context.Request.HasJsonContentType())
			{
				return Error(StatusCodes.Status415UnsupportedMediaType, CouponResultCodes.BadRequest, "Content-Type must be application/json");
			}

			var services = context.RequestServices;
			var maxBytes = services.GetRequiredService<IOptions<CouponServerOptions>>().Value.MaxRequestBytes;
			var body = context.Request.ContentLength > maxBytes ? null : await ReadBodyAsync(context.Request.Body, maxBytes, context.RequestAborted);

			if (body == null)
			{
				return Error(StatusCodes.Status413PayloadTooLarge, CouponResultCodes.BadRequest, $"Body is larger than {maxBytes} bytes");
			}

			CouponRedeemRequest request;

			try
			{
				request = JsonSerializer.Deserialize<CouponRedeemRequest>(body, CouponServerJson.Options);
			}
			catch (JsonException e)
			{
				return Error(StatusCodes.Status400BadRequest, CouponResultCodes.BadRequest, $"Invalid JSON: {e.Message}");
			}

			if (request?.Code == null)
			{
				return Error(StatusCodes.Status400BadRequest, CouponResultCodes.BadRequest, "Code is required");
			}

			var reported = new CouponContext(accountId, request.Platform, request.AppVersion, request.Country);
			var resolver = services.GetService<ICouponContextResolver>();
			var resolved = resolver == null ? reported : await resolver.ResolveAsync(context, reported, context.RequestAborted);

			if (resolved == null || !string.Equals(resolved.AccountId, accountId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("ICouponContextResolver must return a context of the authenticated account");
			}

			var outcome = await services.GetRequiredService<ICouponRedeemer>().RedeemAsync(request.Code, resolved, context.RequestAborted);
			var response = ToResponse(outcome);

			if (outcome.Code != CouponResultCodes.TooManyAttempts)
			{
				return Results.Json(response, CouponServerJson.Options);
			}

			context.Response.Headers.RetryAfter = outcome.RetryAfterSeconds?.ToString(CultureInfo.InvariantCulture);
			return Results.Json(response, CouponServerJson.Options, statusCode: StatusCodes.Status429TooManyRequests);
		}

		internal static CouponRedeemResponse<JsonElement?> ToResponse(CouponRedeemOutcome outcome)
		{
			var record = outcome.Record;
			return new CouponRedeemResponse<JsonElement?>
			{
				Code = outcome.Code,
				Message = outcome.Message,
				CampaignId = record?.CampaignId,
				Grant = record?.GrantJson == null ? (JsonElement?) null : ParseGrant(record.GrantJson),
				RedeemedAt = record?.RedeemedAt,
				RetryAfterSeconds = outcome.RetryAfterSeconds
			};
		}

		private static JsonElement ParseGrant(string json)
		{
			// 문서는 풀에서 빌린 버퍼를 쓰므로 복사본만 남기고 바로 돌려준다
			using (var document = JsonDocument.Parse(json))
			{
				return document.RootElement.Clone();
			}
		}

		private static IResult Error(int httpStatus, int code, string message)
		{
			return Results.Json(new CouponRedeemResponse<JsonElement?> { Code = code, Message = message }, CouponServerJson.Options, statusCode: httpStatus);
		}

		/// <summary>본문을 읽습니다. <paramref name="maxBytes"/> 를 넘으면 null 입니다(Content-Length 없이 보낸 본문도 막음).</summary>
		private static async Task<byte[]> ReadBodyAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
		{
			using (var buffer = new MemoryStream())
			{
				var chunk = new byte[4 * 1024];
				int read;

				while ((read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
				{
					if (buffer.Length + read > maxBytes)
					{
						return null;
					}

					buffer.Write(chunk, 0, read);
				}

				return buffer.ToArray();
			}
		}
	}
}
