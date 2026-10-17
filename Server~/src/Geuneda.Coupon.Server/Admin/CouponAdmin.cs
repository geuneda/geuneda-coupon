using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 운영 도구가 캠페인과 개별 코드를 등록하는 입구입니다(요청 범위). 값을 검사하고 코드를 비교용 모양으로 다듬은 뒤 저장소에 넘깁니다. 관리자 인증은 이것을 부르는 게임 코드가 맡습니다.
	/// </summary>
	public sealed class CouponAdmin
	{
		private readonly ICouponStore _store;

		public CouponAdmin(ICouponStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>캠페인을 넣습니다. ID 나 공용 코드가 이미 있으면 넣지 않고 그 결과입니다(공용 코드와 개별 코드는 한 이름 공간).</summary>
		public Task<CouponAddResult> AddCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken = default)
		{
			if (campaign == null)
			{
				throw new ArgumentNullException(nameof(campaign));
			}

			return _store.AddCampaignAsync(campaign, cancellationToken);
		}

		/// <summary>
		/// 같은 ID 의 캠페인을 바꿉니다(켜기·끄기, 기간, 한도, 조건, 보상). 없으면 false 입니다. 이미 쓴 계정의 기록과 지급은 그대로입니다.
		/// </summary>
		/// <exception cref="ArgumentException">종류나 공용 코드를 바꾸려는 경우(코드를 바꾸려면 새 캠페인)</exception>
		public async Task<bool> UpdateCampaignAsync(CouponCampaign campaign, CancellationToken cancellationToken = default)
		{
			if (campaign == null)
			{
				throw new ArgumentNullException(nameof(campaign));
			}

			var current = await _store.GetCampaignAsync(campaign.Id, cancellationToken);

			if (current == null)
			{
				return false;
			}

			if (current.Kind != campaign.Kind || !string.Equals(current.SharedCode, campaign.SharedCode, StringComparison.Ordinal))
			{
				throw new ArgumentException($"{campaign} cannot change its kind or shared code (create a new campaign)", nameof(campaign));
			}

			return await _store.UpdateCampaignAsync(campaign, cancellationToken);
		}

		/// <summary>
		/// 개별 코드 캠페인에 코드를 넣습니다. 코드는 비교용 모양으로 다듬고(공백·<c>-</c> 제거, 대문자), 입력 안에서 겹치거나 이미 어딘가 있는 코드는 넣지 않고 결과의
		/// <see cref="CouponCodesAddResult.Duplicates"/> 에 알립니다. 코드는 맞히기 어렵게 만드세요(예: 혼동 글자 0·O·1·I 를 뺀 32 글자에서 12자 이상 무작위).
		/// </summary>
		/// <exception cref="ArgumentException">코드 형식이 아닌 값이 있는 경우(아무것도 넣지 않음)</exception>
		/// <exception cref="InvalidOperationException">캠페인이 없거나 개별 코드 캠페인이 아닌 경우</exception>
		public async Task<CouponCodesAddResult> AddCodesAsync(string campaignId, IEnumerable<string> codes, CancellationToken cancellationToken = default)
		{
			if (codes == null)
			{
				throw new ArgumentNullException(nameof(codes));
			}

			var normalized = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var repeated = new List<string>();

			foreach (var code in codes)
			{
				if (!CouponCodes.TryNormalize(code, out var value))
				{
					throw new ArgumentException(
						$"'{code}' is not a coupon code ({CouponCodes.MinLength}-{CouponCodes.MaxLength} letters and digits, spaces and '-' are ignored)", nameof(codes));
				}

				if (seen.Add(value))
				{
					normalized.Add(value);
				}
				else
				{
					repeated.Add(value);
				}
			}

			var campaign = await _store.GetCampaignAsync(campaignId, cancellationToken) ??
				throw new InvalidOperationException($"Campaign {campaignId} does not exist");

			if (campaign.Kind != CouponKind.Unique)
			{
				throw new InvalidOperationException($"{campaign} is not a unique-code campaign");
			}

			var added = await _store.AddCodesAsync(campaign.Id, normalized.AsReadOnly(), cancellationToken);

			if (repeated.Count == 0)
			{
				return added;
			}

			var duplicates = new List<string>(added.Duplicates);
			duplicates.AddRange(repeated);
			return new CouponCodesAddResult(added.Added, duplicates.AsReadOnly());
		}
	}
}
