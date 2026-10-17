using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Geuneda.Coupon.Server;

namespace Geuneda.Coupon.SampleServer
{
	/// <summary>
	/// 샘플 지급자입니다. 캠페인 보상(<c>Reward</c>, 젬 개수)을 메모리 지갑에 더하고 보상 목록과 잔액을 돌려줍니다. 보상 0 은 거절합니다(지급 거절 확인용).
	/// 지갑은 메모리 저장소의 트랜잭션에 들지 않아, 지갑을 바꾼 뒤 실패하면 되돌려지지 않습니다(샘플이라 그대로 둠, <c>InMemoryCouponStore</c> 참고).
	/// </summary>
	public sealed class SampleWallet : ICouponGrantor
	{
		private readonly ConcurrentDictionary<string, int> _gems = new ConcurrentDictionary<string, int>();

		public Task<CouponGrantResult> GrantAsync(CouponGrant grant, CancellationToken cancellationToken)
		{
			var gems = int.Parse(grant.Campaign.Reward ?? "0", CultureInfo.InvariantCulture);

			if (gems <= 0)
			{
				return Task.FromResult(CouponGrantResult.Rejected("No reward configured"));
			}

			var balance = _gems.AddOrUpdate(grant.AccountId, gems, (_, current) => current + gems);
			return Task.FromResult(CouponGrantResult.Granted(new SampleGrant { Items = new[] { new SampleItem { Id = "gem", Amount = gems } }, Gems = balance }));
		}
	}

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
}
