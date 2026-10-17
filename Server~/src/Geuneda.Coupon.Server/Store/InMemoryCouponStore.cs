using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 메모리 저장소입니다. 개발·테스트용입니다. 서버를 다시 시작하면 기록이 사라집니다. 사용 처리는 한 번에 하나씩 합니다(지급 콜백을 기다리는 동안 다른 사용 처리가 기다림).
	/// </summary>
	/// <remarks>
	/// <b>지급을 롤백하지 않습니다.</b> 보장하는 것은 '지급이 실패하면 사용 기록을 넣지 않음'뿐입니다. 지급자가 게임 상태(DB 든 메모리 지갑이든)를 바꾼 뒤 예외를 던지거나
	/// 지급 결과를 JSON 으로 바꾸지 못하면, 바뀐 상태는 남고 기록은 없어 같은 코드로 다시 지급됩니다. 지급과 기록을 함께 되돌리려면 게임 DB 로 <see cref="ICouponStore"/> 를 구현하고
	/// 지급자를 같은 트랜잭션에 넣습니다.
	/// </remarks>
	public sealed class InMemoryCouponStore : ICouponStore
	{
		private readonly object _gate = new object();
		private readonly SemaphoreSlim _redeem = new SemaphoreSlim(1, 1);
		private readonly Dictionary<string, CouponCampaign> _campaigns = new Dictionary<string, CouponCampaign>(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _sharedCodes = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, UniqueCode> _uniqueCodes = new Dictionary<string, UniqueCode>(StringComparer.Ordinal);
		private readonly Dictionary<(string CampaignId, string AccountId), CouponRedemptionRecord> _redemptions =
			new Dictionary<(string CampaignId, string AccountId), CouponRedemptionRecord>();
		private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

		private sealed class UniqueCode
		{
			public string CampaignId;
			public string UsedBy;
		}

		/// <summary>캠페인 사용 수입니다(테스트·운영 확인용).</summary>
		public int GetRedemptionCount(string campaignId)
		{
			lock (_gate)
			{
				return _counts.TryGetValue(campaignId, out var count) ? count : 0;
			}
		}

		public Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken)
		{
			if (campaign == null)
			{
				throw new ArgumentNullException(nameof(campaign));
			}

			lock (_gate)
			{
				if (_campaigns.ContainsKey(campaign.Id))
				{
					return Task.FromResult(CouponAddResult.DuplicateId);
				}

				if (campaign.SharedCode != null && IsCodeTaken(campaign.SharedCode))
				{
					return Task.FromResult(CouponAddResult.DuplicateCode);
				}

				_campaigns.Add(campaign.Id, campaign);

				if (campaign.SharedCode != null)
				{
					_sharedCodes.Add(campaign.SharedCode, campaign.Id);
				}

				return Task.FromResult(CouponAddResult.Added);
			}
		}

		public Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken)
		{
			if (campaign == null)
			{
				throw new ArgumentNullException(nameof(campaign));
			}

			lock (_gate)
			{
				if (!_campaigns.ContainsKey(campaign.Id))
				{
					return Task.FromResult(false);
				}

				_campaigns[campaign.Id] = campaign;
				return Task.FromResult(true);
			}
		}

		public Task<CouponCampaign> GetCampaignAsync(string campaignId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_campaigns.TryGetValue(campaignId, out var campaign);
				return Task.FromResult(campaign);
			}
		}

		public Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IReadOnlyList<string> codes, CancellationToken cancellationToken)
		{
			if (codes == null)
			{
				throw new ArgumentNullException(nameof(codes));
			}

			lock (_gate)
			{
				if (!_campaigns.ContainsKey(campaignId))
				{
					throw new InvalidOperationException($"Campaign {campaignId} does not exist");
				}

				var added = 0;
				var duplicates = new List<string>();

				foreach (var code in codes)
				{
					if (IsCodeTaken(code))
					{
						duplicates.Add(code);
						continue;
					}

					_uniqueCodes.Add(code, new UniqueCode { CampaignId = campaignId });
					added++;
				}

				return Task.FromResult(new CouponCodesAddResult(added, duplicates.AsReadOnly()));
			}
		}

		public Task<CouponCodeMatch> FindCodeAsync(string code, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (_sharedCodes.TryGetValue(code, out var sharedId))
				{
					return Task.FromResult(new CouponCodeMatch(_campaigns[sharedId], code));
				}

				return Task.FromResult(_uniqueCodes.TryGetValue(code, out var unique) ? new CouponCodeMatch(_campaigns[unique.CampaignId], code) : null);
			}
		}

		public Task<CouponRedemptionRecord> FindRedemptionAsync(string campaignId, string accountId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_redemptions.TryGetValue((campaignId, accountId), out var record);
				return Task.FromResult(record);
			}
		}

		public async Task<CouponStoreRedeemResult> RedeemAsync(CouponRedeemAttempt attempt, Func<CancellationToken, Task<CouponStoreGrant>> grant,
			CancellationToken cancellationToken)
		{
			if (attempt == null)
			{
				throw new ArgumentNullException(nameof(attempt));
			}

			if (grant == null)
			{
				throw new ArgumentNullException(nameof(grant));
			}

			await _redeem.WaitAsync(cancellationToken);

			try
			{
				var key = (attempt.Campaign.Id, attempt.AccountId);
				UniqueCode unique;

				lock (_gate)
				{
					if (_redemptions.TryGetValue(key, out var existing))
					{
						return CouponStoreRedeemResult.Duplicate(existing);
					}

					if (_uniqueCodes.TryGetValue(attempt.Code, out unique) && unique.UsedBy != null)
					{
						return CouponStoreRedeemResult.CodeUsed();
					}

					// 한도는 지금 저장된 캠페인으로 센다(사용 처리 중에 운영자가 바꿨을 수 있음)
					var campaign = _campaigns.TryGetValue(attempt.Campaign.Id, out var stored) ? stored : attempt.Campaign;
					var count = _counts.TryGetValue(campaign.Id, out var current) ? current : 0;

					if (campaign.MaxRedemptions.HasValue && count >= campaign.MaxRedemptions.Value)
					{
						return CouponStoreRedeemResult.SoldOut();
					}
				}

				var granted = await grant(cancellationToken);

				if (!granted.IsGranted)
				{
					return CouponStoreRedeemResult.NotGranted();
				}

				var record = new CouponRedemptionRecord(attempt.Campaign.Id, attempt.AccountId, attempt.Code, attempt.RedeemedAt, granted.GrantJson);

				lock (_gate)
				{
					_redemptions.Add(key, record);
					_counts[attempt.Campaign.Id] = (_counts.TryGetValue(attempt.Campaign.Id, out var current) ? current : 0) + 1;

					if (unique != null)
					{
						unique.UsedBy = attempt.AccountId;
					}
				}

				return CouponStoreRedeemResult.Recorded(record);
			}
			finally
			{
				_redeem.Release();
			}
		}

		private bool IsCodeTaken(string code)
		{
			return _sharedCodes.ContainsKey(code) || _uniqueCodes.ContainsKey(code);
		}
	}
}
