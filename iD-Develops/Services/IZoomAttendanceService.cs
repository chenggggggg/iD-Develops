using iD_Develops.Enums;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public sealed record ZoomWebhookHandlingResult(
        bool Success,
        int StatusCode,
        string? PlainToken,
        string? EncryptedToken,
        string? ErrorMessage);

    public interface IZoomAttendanceService
    {
        Task<ZoomWebhookHandlingResult> HandleWebhookAsync(
            string rawBody,
            string? requestTimestamp,
            string? signature,
            CancellationToken cancellationToken = default);

        Task<int> ReconcileDueMeetingsAsync(CancellationToken cancellationToken = default);

        Task<OperationResult> OverrideAttendanceAsync(
            ScheduleActor actor,
            int eventBookingId,
            EventBookingStatus status,
            CancellationToken cancellationToken = default);
    }
}
