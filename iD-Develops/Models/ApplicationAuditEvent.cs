using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models;

[Table("ApplicationAuditEvents")]
public sealed class ApplicationAuditEvent
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public AuditEventCategory Category { get; set; }

    public AuditEventOutcome Outcome { get; set; }

    [Required, MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ActorUserId { get; set; }

    [MaxLength(450)]
    public string? SubjectUserId { get; set; }

    [MaxLength(100)]
    public string? EntityType { get; set; }

    [MaxLength(200)]
    public string? EntityId { get; set; }

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    [MaxLength(200)]
    public string? DeduplicationKey { get; set; }

    [MaxLength(500)]
    public string? RequestPath { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [Column(TypeName = "jsonb")]
    public string? MetadataJson { get; set; }
}
