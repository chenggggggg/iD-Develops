using System.Diagnostics;

namespace iD_Develops.E2ETests;

// Browser contexts share localhost's production rate-limit partition. Pace their
// public product posts while reserving concurrent pairs together; assertions and
// server-side limits still run normally.
internal static class PublicProductSubmissionPacing
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Queue<long> Reservations = new();

    public static async Task ReserveAsync(int count = 1)
    {
        await Gate.WaitAsync();
        try
        {
            while (true)
            {
                while (Reservations.TryPeek(out var oldest) && Stopwatch.GetElapsedTime(oldest) >= TimeSpan.FromSeconds(63))
                    Reservations.Dequeue();
                if (Reservations.Count + count <= 8) break;
                var delay = TimeSpan.FromSeconds(63) - Stopwatch.GetElapsedTime(Reservations.Peek());
                await Task.Delay(delay < TimeSpan.FromSeconds(30) ? delay : TimeSpan.FromSeconds(30));
            }
            for (var index = 0; index < count; index++) Reservations.Enqueue(Stopwatch.GetTimestamp());
        }
        finally { Gate.Release(); }
    }
}
