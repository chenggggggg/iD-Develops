using iD_Develops.Services;

namespace iD_Develops.Tests.Services;

public sealed class BackgroundOperationStatusStoreTests
{
    [Fact]
    public void ReportSuccess_UpdatesCurrentStatusAndOnlyRecordsMeaningfulActivity()
    {
        var store = new BackgroundOperationStatusStore();
        var startedAtUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

        store.ReportSuccess(
            "cleanup",
            "Cleanup",
            startedAtUtc,
            startedAtUtc.AddSeconds(2),
            startedAtUtc.AddMinutes(5),
            0,
            "Nothing to update.");

        var operation = Assert.Single(store.GetOperations());
        Assert.Equal(BackgroundOperationState.Healthy, operation.State);
        Assert.Equal(0, operation.AffectedCount);
        Assert.Empty(store.GetRecentEvents());

        store.ReportSuccess(
            "cleanup",
            "Cleanup",
            startedAtUtc.AddMinutes(5),
            startedAtUtc.AddMinutes(5).AddSeconds(1),
            startedAtUtc.AddMinutes(10),
            3,
            "Updated 3 records.");

        operation = Assert.Single(store.GetOperations());
        Assert.Equal(3, operation.AffectedCount);
        var operationEvent = Assert.Single(store.GetRecentEvents());
        Assert.Equal("Updated 3 records.", operationEvent.Message);
    }

    [Fact]
    public void Failure_PreservesLastSuccessfulRunDetails()
    {
        var store = new BackgroundOperationStatusStore();
        var startedAtUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(1);

        store.ReportSuccess(
            "cleanup",
            "Cleanup",
            startedAtUtc,
            completedAtUtc,
            completedAtUtc.AddMinutes(5),
            2,
            "Updated 2 records.");
        store.ReportFailure(
            "cleanup",
            "Cleanup",
            completedAtUtc.AddMinutes(1),
            "The cleanup failed.");

        var operation = Assert.Single(store.GetOperations());
        Assert.Equal(BackgroundOperationState.Failed, operation.State);
        Assert.Equal(startedAtUtc, operation.LastStartedAtUtc);
        Assert.Equal(completedAtUtc, operation.LastCompletedAtUtc);
        Assert.Equal(2, operation.AffectedCount);
        Assert.Equal(2, store.GetRecentEvents().Count);
    }
}
