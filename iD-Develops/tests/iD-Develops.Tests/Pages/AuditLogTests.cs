using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Admin;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Tests.Pages;

public sealed class AuditLogTests
{
    [Fact]
    public async Task DefaultDayUsesAmsterdamRatherThanUtcDate()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 1, 22, 30, 0, TimeSpan.Zero));
        db.ApplicationAuditEvents.AddRange(
            Event(new DateTime(2026, 7, 1, 21, 59, 59, DateTimeKind.Utc)),
            Event(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc)),
            Event(new DateTime(2026, 7, 2, 21, 59, 59, DateTimeKind.Utc)),
            Event(new DateTime(2026, 7, 2, 22, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var model = Page(db, clock);

        await model.OnGetAsync(default);

        Assert.Equal(new DateOnly(2026, 7, 2), model.From);
        Assert.Equal(model.From, model.To);
        Assert.Equal(2, model.TotalCount);
        Assert.All(model.Events, item => Assert.Equal(2, model.LocalTime(item.OccurredAtUtc).Day));
        Assert.All(model.Events, item => Assert.Equal("CEST", model.ZoneLabel(item.OccurredAtUtc)));
    }

    [Theory]
    [InlineData(2026, 3, 29, "2026-03-28T23:00:00Z", 23)]
    [InlineData(2026, 10, 25, "2026-10-24T22:00:00Z", 25)]
    public async Task DateFiltersIncludeTheWholeDstDayWithAnExclusiveEnd(int year, int month, int day, string start, int hours)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var startUtc = DateTimeOffset.Parse(start).UtcDateTime;
        var endUtc = startUtc.AddHours(hours);
        db.ApplicationAuditEvents.AddRange(Event(startUtc.AddTicks(-1)), Event(startUtc), Event(endUtc.AddTicks(-1)), Event(endUtc));
        await db.SaveChangesAsync();
        var model = Page(db);
        model.From = model.To = new DateOnly(year, month, day);

        await model.OnGetAsync(default);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(new[] { endUtc.AddTicks(-1), startUtc }, model.Events.Select(item => item.OccurredAtUtc));
    }

    [Fact]
    public async Task InvalidOrReversedDatesReturnValidationWithoutRunningAnUnboundedQuery()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        db.ApplicationAuditEvents.Add(Event(DateTime.UtcNow));
        await db.SaveChangesAsync();
        var model = Page(db);
        model.From = new DateOnly(2026, 11, 4);
        model.To = new DateOnly(2026, 11, 3);

        var result = await model.OnGetMoreAsync(default);

        Assert.False(model.ModelState.IsValid);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(model.Events);
        Assert.Equal(0, model.TotalCount);
    }

    [Fact]
    public async Task AllDatesCanFindOldEventsAndSingleDateBecomesADayFilter()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        db.ApplicationAuditEvents.AddRange(Event(new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc)), Event(DateTime.UtcNow));
        await db.SaveChangesAsync();
        var model = Page(db);
        model.AllDates = true;
        await model.OnGetAsync(default);
        Assert.Equal(2, model.TotalCount);
        model = Page(db);
        model.To = new DateOnly(2020, 1, 1);
        await model.OnGetAsync(default);
        Assert.Single(model.Events);
        Assert.Equal(model.From, model.To);
        Assert.Equal("CET", model.ZoneLabel(model.Events[0].OccurredAtUtc));
    }

    [Fact]
    public async Task CursorPreservesOrderAcrossEqualTimestampsAndConcurrentNewEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var time = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        db.ApplicationAuditEvents.AddRange(Enumerable.Range(0, 56).Select(_ => Event(time)));
        await db.SaveChangesAsync();
        var first = Page(db);
        first.AllDates = true;
        first.PageSize = 25;
        await first.OnGetAsync(default);
        Assert.Equal(56, first.TotalCount);
        Assert.Equal(25, first.Events.Count);
        Assert.True(first.HasMore);
        db.ApplicationAuditEvents.Add(Event(time.AddSeconds(1)));
        await db.SaveChangesAsync();
        var second = Page(db);
        second.AllDates = true;
        second.PageSize = 25;
        second.Before = new DateTimeOffset(first.Events[^1].OccurredAtUtc, TimeSpan.Zero);
        second.BeforeId = first.Events[^1].Id;
        await second.OnGetAsync(default);
        Assert.Equal(57, second.TotalCount);
        Assert.Equal(25, second.Events.Count);
        Assert.True(second.HasMore);
        Assert.Empty(first.Events.Select(item => item.Id).Intersect(second.Events.Select(item => item.Id)));
        Assert.True(first.Events[^1].Id > second.Events[0].Id);
        var third = Page(db);
        third.AllDates = true;
        third.PageSize = 25;
        third.Before = new DateTimeOffset(second.Events[^1].OccurredAtUtc, TimeSpan.Zero);
        third.BeforeId = second.Events[^1].Id;
        await third.OnGetAsync(default);
        Assert.Equal(6, third.Events.Count);
        Assert.False(third.HasMore);
    }

    [Fact]
    public async Task BatchSizeIsBoundedAndInvalidCursorIsRejected()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        db.ApplicationAuditEvents.AddRange(Enumerable.Range(0, 205).Select(_ => Event(DateTime.UtcNow)));
        await db.SaveChangesAsync();
        var model = Page(db);
        model.AllDates = true;
        model.PageSize = int.MaxValue;
        await model.OnGetAsync(default);
        Assert.Equal(100, model.PageSize);
        Assert.Equal(100, model.Events.Count);
        Assert.True(model.HasMore);
        model = Page(db);
        model.AllDates = true;
        model.BeforeId = 1;
        await model.OnGetAsync(default);
        Assert.False(model.ModelState.IsValid);
        Assert.Empty(model.Events);
    }

    private static AuditLogModel Page(iD_Develops.Data.ApplicationDbContext db, TimeProvider? clock = null)
        => new(db, clock) { PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };

    private static ApplicationAuditEvent Event(DateTime time) => new()
    {
        OccurredAtUtc = time,
        EventType = "Test.Event",
        Summary = "Audit event",
        Category = AuditEventCategory.System
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
