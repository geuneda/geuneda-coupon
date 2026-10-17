using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Coupon.Server
{
	/// <summary>
	/// 메모리 실패 기록입니다. 서버 한 대에서만 맞습니다(여러 대면 서버마다 따로 셈). 다시 시작하면 사라집니다.
	/// 예약할 때 그 계정의 지난 기록을 지우고, 계정이 많아지면 지난 기록만 남은 계정을 함께 지웁니다.
	/// </summary>
	public sealed class InMemoryCouponAttemptStore : ICouponAttemptStore
	{
		internal const int SweepThreshold = 10000;

		private readonly object _gate = new object();
		private readonly Dictionary<string, List<Entry>> _entries = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
		private long _nextId;

		private readonly struct Entry
		{
			public Entry(string id, DateTimeOffset at)
			{
				Id = id;
				At = at;
			}

			public string Id { get; }
			public DateTimeOffset At { get; }
		}

		internal int AccountCount
		{
			get
			{
				lock (_gate)
				{
					return _entries.Count;
				}
			}
		}

		public Task<CouponAttemptReservation> TryReserveAsync(string accountId, DateTimeOffset at, DateTimeOffset since, int limit,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (!_entries.TryGetValue(accountId, out var entries))
				{
					if (_entries.Count >= SweepThreshold)
					{
						Sweep(since);
					}

					entries = new List<Entry>();
					_entries.Add(accountId, entries);
				}

				entries.RemoveAll(entry => entry.At < since);

				if (entries.Count >= limit)
				{
					var times = new List<DateTimeOffset>(entries.Count);

					foreach (var entry in entries)
					{
						times.Add(entry.At);
					}

					return Task.FromResult(CouponAttemptReservation.Denied(times.AsReadOnly()));
				}

				var id = (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
				entries.Add(new Entry(id, at));
				return Task.FromResult(CouponAttemptReservation.Reserved(id));
			}
		}

		public Task ReleaseAsync(string accountId, string reservationId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (_entries.TryGetValue(accountId, out var entries))
				{
					entries.RemoveAll(entry => entry.Id == reservationId);
				}

				return Task.CompletedTask;
			}
		}

		private void Sweep(DateTimeOffset since)
		{
			var stale = new List<string>();

			foreach (var pair in _entries)
			{
				pair.Value.RemoveAll(entry => entry.At < since);

				if (pair.Value.Count == 0)
				{
					stale.Add(pair.Key);
				}
			}

			foreach (var accountId in stale)
			{
				_entries.Remove(accountId);
			}
		}
	}
}
