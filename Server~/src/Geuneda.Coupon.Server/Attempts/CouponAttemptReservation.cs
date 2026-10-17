using System;
using System.Collections.Generic;

namespace Geuneda.Coupon.Server
{
	/// <summary><see cref="ICouponAttemptStore.TryReserveAsync"/> 의 결과입니다.</summary>
	public sealed class CouponAttemptReservation
	{
		private static readonly IReadOnlyList<DateTimeOffset> NoEntries = Array.Empty<DateTimeOffset>();

		private CouponAttemptReservation(string reservationId, IReadOnlyList<DateTimeOffset> entries)
		{
			ReservationId = reservationId;
			Entries = entries;
		}

		/// <summary>넣은 기록의 ID 입니다. 거절이면 null 입니다.</summary>
		public string ReservationId { get; }

		public bool IsReserved => ReservationId != null;

		/// <summary>거절일 때 창 안 기록 시각들(순서 무관)입니다. 잠금이 풀리는 시각을 셉니다. 예약이면 비어 있습니다.</summary>
		public IReadOnlyList<DateTimeOffset> Entries { get; }

		public static CouponAttemptReservation Reserved(string reservationId)
		{
			if (string.IsNullOrEmpty(reservationId))
			{
				throw new ArgumentException("Reservation id is required", nameof(reservationId));
			}

			return new CouponAttemptReservation(reservationId, NoEntries);
		}

		public static CouponAttemptReservation Denied(IReadOnlyList<DateTimeOffset> entries)
		{
			if (entries == null || entries.Count == 0)
			{
				throw new ArgumentException("A denial needs the entries in the window", nameof(entries));
			}

			return new CouponAttemptReservation(null, entries);
		}
	}
}
