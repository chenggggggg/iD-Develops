namespace iD_Develops.Services;

public static class ApplicationAuditEventTypes
{
    public const string LoginSucceeded = "authentication.login.succeeded";
    public const string LoginFailed = "authentication.login.failed";
    public const string Logout = "authentication.logout";
    public const string PurchaseCompleted = "commerce.purchase.completed";
    public const string PurchaseFailed = "commerce.purchase.failed";
    public const string CheckoutSessionCreationFailed = "commerce.checkout.session_creation_failed";
    public const string ExamAttemptStarted = "examination.attempt.started";
    public const string ExamAttemptStatusChanged = "examination.attempt.status_changed";
    public const string CourseAccessGranted = "course.access.granted";
    public const string CourseAccessChanged = "course.access.changed";
    public const string CourseAccessRevoked = "course.access.revoked";
    public const string ExamAssigned = "examination.assignment.created";
    public const string ExamUnassigned = "examination.assignment.removed";
    public const string BookingCreated = "scheduling.booking.created";
    public const string BookingStatusChanged = "scheduling.booking.status_changed";
    public const string CreditTransactionRecorded = "commerce.credit.transaction_recorded";
    public const string RoleAssigned = "administration.role.assigned";
    public const string RoleRemoved = "administration.role.removed";
}
