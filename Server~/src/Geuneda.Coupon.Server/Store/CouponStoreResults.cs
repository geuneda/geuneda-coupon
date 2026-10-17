using System;
using System.Collections.Generic;

namespace Geuneda.Coupon.Server
{
	/// <summary>캠페인 등록 결과입니다.</summary>
	public enum CouponAddResult
	{
		Added = 1,

		/// <summary>같은 ID 의 캠페인이 있습니다</summary>
		DuplicateId = 2,

		/// <summary>공용 코드가 다른 캠페인의 공용 코드나 개별 코드와 같습니다</summary>
		DuplicateCode = 3,
	}

	/// <summary>개별 코드 추가 결과입니다.</summary>
	public sealed class CouponCodesAddResult
	{
		public CouponCodesAddResult(int added, IReadOnlyList<string> duplicates)
		{
			Added = added;
			Duplicates = duplicates ?? throw new ArgumentNullException(nameof(duplicates));
		}

		/// <summary>새로 넣은 코드 수입니다.</summary>
		public int Added { get; }

		/// <summary>이미 어딘가(이 캠페인, 다른 캠페인의 개별 코드, 공용 코드)에 있어 넣지 않은 코드(비교용 모양)입니다.</summary>
		public IReadOnlyList<string> Duplicates { get; }
	}

	/// <summary><see cref="ICouponStore.RedeemAsync"/> 의 결과 종류입니다.</summary>
	public enum CouponStoreOutcome
	{
		/// <summary>지급하고 기록했습니다</summary>
		Recorded = 1,

		/// <summary>이 계정이 이 캠페인을 이미 썼습니다(있던 기록)</summary>
		Duplicate = 2,

		/// <summary>개별 코드를 다른 계정이 이미 썼습니다</summary>
		CodeUsed = 3,

		/// <summary>캠페인 전체 사용 한도에 닿았습니다</summary>
		SoldOut = 4,

		/// <summary>지급 콜백이 지급하지 않았습니다(롤백)</summary>
		NotGranted = 5,
	}

	/// <summary><see cref="ICouponStore.RedeemAsync"/> 의 결과입니다.</summary>
	public readonly struct CouponStoreRedeemResult
	{
		private CouponStoreRedeemResult(CouponStoreOutcome outcome, CouponRedemptionRecord record)
		{
			Outcome = outcome;
			Record = record;
		}

		public CouponStoreOutcome Outcome { get; }

		/// <summary><see cref="CouponStoreOutcome.Recorded"/> 면 넣은 기록, <see cref="CouponStoreOutcome.Duplicate"/> 면 있던 기록, 그 밖은 null 입니다.</summary>
		public CouponRedemptionRecord Record { get; }

		public static CouponStoreRedeemResult Recorded(CouponRedemptionRecord record)
		{
			return new CouponStoreRedeemResult(CouponStoreOutcome.Recorded, record ?? throw new ArgumentNullException(nameof(record)));
		}

		public static CouponStoreRedeemResult Duplicate(CouponRedemptionRecord record)
		{
			return new CouponStoreRedeemResult(CouponStoreOutcome.Duplicate, record ?? throw new ArgumentNullException(nameof(record)));
		}

		public static CouponStoreRedeemResult CodeUsed()
		{
			return new CouponStoreRedeemResult(CouponStoreOutcome.CodeUsed, null);
		}

		public static CouponStoreRedeemResult SoldOut()
		{
			return new CouponStoreRedeemResult(CouponStoreOutcome.SoldOut, null);
		}

		public static CouponStoreRedeemResult NotGranted()
		{
			return new CouponStoreRedeemResult(CouponStoreOutcome.NotGranted, null);
		}
	}

	/// <summary>지급 콜백의 결과입니다. 지급했으면 기록에 넣을 지급 결과 JSON 을 담습니다.</summary>
	public readonly struct CouponStoreGrant
	{
		private CouponStoreGrant(bool isGranted, string grantJson)
		{
			IsGranted = isGranted;
			GrantJson = grantJson;
		}

		public bool IsGranted { get; }

		/// <summary>기록에 넣을 지급 결과 JSON. 없으면 null</summary>
		public string GrantJson { get; }

		public static CouponStoreGrant Granted(string grantJson)
		{
			return new CouponStoreGrant(true, grantJson);
		}

		public static CouponStoreGrant NotGranted()
		{
			return new CouponStoreGrant(false, null);
		}
	}
}
