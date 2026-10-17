using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Coupon.Server.Tests
{
	public class AdminTest
	{
		private static readonly CancellationToken None = CancellationToken.None;

		private static CouponAdmin NewAdmin(out InMemoryCouponStore store)
		{
			store = new InMemoryCouponStore();
			return new CouponAdmin(store);
		}

		[Fact]
		public async Task AddCampaign_RejectsDuplicateIdsAndCodes()
		{
			var admin = NewAdmin(out _);

			Assert.Equal(CouponAddResult.Added, await admin.AddCampaignAsync(new CouponCampaign("a", CouponKind.Shared, "CODE-0001")));
			Assert.Equal(CouponAddResult.DuplicateId, await admin.AddCampaignAsync(new CouponCampaign("a", CouponKind.Shared, "CODE0002")));
			Assert.Equal(CouponAddResult.DuplicateCode, await admin.AddCampaignAsync(new CouponCampaign("b", CouponKind.Shared, "code 0001")));

			Assert.Equal(CouponAddResult.Added, await admin.AddCampaignAsync(new CouponCampaign("u", CouponKind.Unique)));
			await admin.AddCodesAsync("u", new[] { "UNIQUE0001" });
			Assert.Equal(CouponAddResult.DuplicateCode, await admin.AddCampaignAsync(new CouponCampaign("c", CouponKind.Shared, "unique-0001")));
		}

		[Fact]
		public async Task AddCodes_NormalizesAndReportsDuplicates()
		{
			var admin = NewAdmin(out var store);
			await admin.AddCampaignAsync(new CouponCampaign("shared", CouponKind.Shared, "SHARED0001"));
			await admin.AddCampaignAsync(new CouponCampaign("u", CouponKind.Unique));
			await admin.AddCampaignAsync(new CouponCampaign("v", CouponKind.Unique));
			await admin.AddCodesAsync("v", new[] { "OTHER0001" });

			var result = await admin.AddCodesAsync("u", new[] { "abcd-efgh", "ABCDEFGH", "shared-0001", "other0001", "NEW00001" });

			Assert.Equal(2, result.Added);
			Assert.Equal(new[] { "ABCDEFGH", "OTHER0001", "SHARED0001" }, result.Duplicates.OrderBy(code => code, StringComparer.Ordinal));
			Assert.Equal("u", (await store.FindCodeAsync("NEW00001", None)).Campaign.Id);
			Assert.Equal("ABCDEFGH", (await store.FindCodeAsync("ABCDEFGH", None)).Code);
		}

		[Fact]
		public async Task AddCodes_Misuse_Throws()
		{
			var admin = NewAdmin(out var store);
			await admin.AddCampaignAsync(new CouponCampaign("shared", CouponKind.Shared, "SHARED0001"));
			await admin.AddCampaignAsync(new CouponCampaign("u", CouponKind.Unique));

			await Assert.ThrowsAsync<InvalidOperationException>(() => admin.AddCodesAsync("missing", new[] { "CODE0001" }));
			await Assert.ThrowsAsync<InvalidOperationException>(() => admin.AddCodesAsync("shared", new[] { "CODE0001" }));
			await Assert.ThrowsAsync<ArgumentException>(() => admin.AddCodesAsync("u", new[] { "GOOD0001", "bad!" }));
			Assert.Null(await store.FindCodeAsync("GOOD0001", None));
			await Assert.ThrowsAsync<ArgumentNullException>(() => admin.AddCodesAsync("u", null));
		}

		[Fact]
		public async Task UpdateCampaign_KeepsKindAndCode()
		{
			var admin = NewAdmin(out var store);
			await admin.AddCampaignAsync(new CouponCampaign("a", CouponKind.Shared, "CODE0001", enabled: true));

			Assert.False(await admin.UpdateCampaignAsync(new CouponCampaign("missing", CouponKind.Shared, "CODE0009")));
			await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdateCampaignAsync(new CouponCampaign("a", CouponKind.Shared, "CODE0002")));
			await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdateCampaignAsync(new CouponCampaign("a", CouponKind.Unique)));

			Assert.True(await admin.UpdateCampaignAsync(new CouponCampaign("a", CouponKind.Shared, "code-0001", enabled: false)));
			Assert.False((await store.GetCampaignAsync("a", None)).Enabled);
		}

		[Fact]
		public void Campaign_ValidatesValues()
		{
			Assert.Throws<ArgumentException>(() => new CouponCampaign("bad id", CouponKind.Shared, "CODE0001"));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", default, "CODE0001"));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", CouponKind.Shared));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", CouponKind.Shared, "ab"));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", CouponKind.Unique, "CODE0001"));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", CouponKind.Unique, startsAt: DateTimeOffset.UnixEpoch, endsAt: DateTimeOffset.UnixEpoch));
			Assert.Throws<ArgumentException>(() => new CouponCampaign("a", CouponKind.Unique, maxRedemptions: 0));

			var campaign = new CouponCampaign("a", CouponKind.Shared, " ab-cd ef ");
			Assert.Equal("ABCDEF", campaign.SharedCode);
			Assert.Same(CouponConditions.None, campaign.Conditions);
			Assert.True(campaign.Enabled);
		}

		[Fact]
		public void Conditions_ValidateValues()
		{
			Assert.Throws<ArgumentException>(() => new CouponConditions(platforms: new string[0]));
			Assert.Throws<ArgumentException>(() => new CouponConditions(platforms: new[] { "Android" }));
			Assert.Throws<ArgumentException>(() => new CouponConditions(platforms: new[] { "ios", "ios" }));
			Assert.Throws<ArgumentException>(() => new CouponConditions(countries: new[] { "KOR" }));
			Assert.Throws<ArgumentException>(() => new CouponConditions(countries: new[] { "kr", "KR" }));
			Assert.Throws<ArgumentException>(() => new CouponConditions(minAppVersion: "1.2.3.4.5"));
			Assert.Throws<ArgumentException>(() => new CouponConditions(minAppVersion: "1.x"));
			Assert.Throws<ArgumentNullException>(() => CouponConditions.None.Allows(null));

			Assert.Equal(new[] { "KR" }, new CouponConditions(countries: new[] { "kr" }).Countries);
			Assert.True(CouponConditions.None.Allows(new CouponContext("a", null, null, null)));
		}

		[Fact]
		public void Context_NormalizesReportedValues()
		{
			var context = new CouponContext("a", " IOS ", " 1.0 ", " kr ");
			Assert.Equal("ios", context.Platform);
			Assert.Equal("1.0", context.AppVersion);
			Assert.Equal("KR", context.Country);

			var junk = new CouponContext("a", "i os", new string('1', 65), "K1");
			Assert.Null(junk.Platform);
			Assert.Null(junk.AppVersion);
			Assert.Null(junk.Country);

			Assert.Throws<ArgumentException>(() => new CouponContext("", null, null, null));
		}

		[Fact]
		public void Codes_Normalize()
		{
			Assert.True(CouponCodes.TryNormalize(" ab-cd\tef ", out var normalized));
			Assert.Equal("ABCDEF", normalized);
			Assert.False(CouponCodes.TryNormalize(null, out _));
			Assert.False(CouponCodes.TryNormalize("abc", out _));
			Assert.False(CouponCodes.TryNormalize(new string('A', 65), out _));
			Assert.True(CouponCodes.TryNormalize(new string('a', 64), out _));
			Assert.False(CouponCodes.TryNormalize("ABCD_EFGH", out _));
			Assert.False(CouponCodes.TryNormalize("ＡＢＣＤ", out _));
			Assert.False(CouponCodes.TryNormalize("ıııı", out _));
		}

		[Fact]
		public async Task AttemptStore_ReservesUnderTheLimit_ReleasesAndSweeps()
		{
			var store = new InMemoryCouponAttemptStore();
			var t = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

			var first = await store.TryReserveAsync("a", t, t.AddHours(-1), 2, None);
			var second = await store.TryReserveAsync("a", t.AddMinutes(30), t.AddMinutes(-30), 2, None);
			Assert.True(first.IsReserved);
			Assert.True(second.IsReserved);
			Assert.NotEqual(first.ReservationId, second.ReservationId);

			var denied = await store.TryReserveAsync("a", t.AddMinutes(40), t.AddMinutes(-20), 2, None);
			Assert.False(denied.IsReserved);
			Assert.Null(denied.ReservationId);
			Assert.Equal(new[] { t, t.AddMinutes(30) }, denied.Entries.OrderBy(at => at));

			await store.ReleaseAsync("a", first.ReservationId, None);
			await store.ReleaseAsync("a", first.ReservationId, None);
			await store.ReleaseAsync("missing", "1", None);
			Assert.True((await store.TryReserveAsync("a", t.AddMinutes(41), t.AddMinutes(-19), 2, None)).IsReserved);

			// 창 밖 기록은 세지 않는다
			Assert.True((await store.TryReserveAsync("a", t.AddHours(2), t.AddHours(1), 2, None)).IsReserved);

			for (var i = 0; i < InMemoryCouponAttemptStore.SweepThreshold; i++)
			{
				await store.TryReserveAsync("idle" + i, t, t.AddHours(-1), 10, None);
			}

			Assert.Equal(InMemoryCouponAttemptStore.SweepThreshold + 1, store.AccountCount);
			await store.TryReserveAsync("new", t.AddHours(5), t.AddHours(4), 10, None);
			Assert.Equal(1, store.AccountCount);
		}

		[Fact]
		public void Reservation_Validates()
		{
			Assert.Throws<ArgumentException>(() => CouponAttemptReservation.Reserved(null));
			Assert.Throws<ArgumentException>(() => CouponAttemptReservation.Denied(Array.Empty<DateTimeOffset>()));
		}

		[Fact]
		public void Map_WithoutRegistrations_Throws()
		{
			var empty = WebApplication.CreateSlimBuilder().Build();
			Assert.Contains("AddGeunedaCoupon", Assert.Throws<InvalidOperationException>(() => empty.MapGeunedaCoupon("/c", _ => Task.FromResult("a"))).Message);

			var noStore = WebApplication.CreateSlimBuilder();
			noStore.Services.AddGeunedaCoupon().AddGrantor<TestGrantor>();
			Assert.Contains("store", Assert.Throws<InvalidOperationException>(() => noStore.Build().MapGeunedaCoupon("/c", _ => Task.FromResult("a"))).Message);

			var noGrantor = WebApplication.CreateSlimBuilder();
			noGrantor.Services.AddGeunedaCoupon().AddInMemoryStore();
			Assert.Contains("grantor", Assert.Throws<InvalidOperationException>(() => noGrantor.Build().MapGeunedaCoupon("/c", _ => Task.FromResult("a"))).Message);
		}

		[Fact]
		public async Task Options_AreValidatedAtStartup()
		{
			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddGeunedaCoupon(options => options.MaxFailures = 0).AddInMemoryStore().AddGrantor<TestGrantor>();
			var app = builder.Build();

			await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync());
		}
	}
}
