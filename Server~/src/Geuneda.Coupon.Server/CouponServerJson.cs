using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geuneda.Coupon.Server
{
	/// <summary>요청·응답과 지급 결과의 JSON 설정입니다. 이름은 camelCase, 읽을 때 대소문자를 가리지 않습니다(클라이언트는 Newtonsoft camelCase).</summary>
	internal static class CouponServerJson
	{
		public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
	}
}
