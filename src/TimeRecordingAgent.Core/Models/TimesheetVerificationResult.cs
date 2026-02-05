using System.Text.Json.Serialization;

namespace TimeRecordingAgent.Core.Models;

/// <summary>
/// Represents the result of verifying a daily timesheet against outside counsel guidelines.
/// </summary>
public sealed record TimesheetVerificationResult(
    /// <summary>
    /// Overall compliance status of the timesheet.
    /// </summary>
    ComplianceStatus OverallStatus,

    /// <summary>
    /// Summary of the verification findings.
    /// </summary>
    string Summary,

    /// <summary>
    /// Individual entry verification results.
    /// </summary>
    IReadOnlyList<EntryVerificationResult> EntryResults,

    /// <summary>
    /// Total billable hours in the timesheet.
    /// </summary>
    double TotalBillableHours,

    /// <summary>
    /// Total non-billable hours in the timesheet.
    /// </summary>
    double TotalNonBillableHours,

    /// <summary>
    /// Timestamp when verification was performed.
    /// </summary>
    DateTime VerifiedAtUtc);

/// <summary>
/// Represents the verification result for a single time entry.
/// </summary>
public sealed record EntryVerificationResult(
    /// <summary>
    /// The ID of the activity record being verified.
    /// </summary>
    long ActivityId,

    /// <summary>
    /// The category assigned to this entry.
    /// </summary>
    string? Category,

    /// <summary>
    /// The description of the work performed.
    /// </summary>
    string? Description,

    /// <summary>
    /// Duration in hours.
    /// </summary>
    double DurationHours,

    /// <summary>
    /// Whether the entry is marked as billable.
    /// </summary>
    bool IsBillable,

    /// <summary>
    /// Compliance status for this entry.
    /// </summary>
    ComplianceStatus Status,

    /// <summary>
    /// List of issues found with this entry.
    /// </summary>
    IReadOnlyList<ComplianceIssue> Issues,

    /// <summary>
    /// Suggestions for improving the entry.
    /// </summary>
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Represents a compliance issue found during verification.
/// </summary>
public sealed record ComplianceIssue(
    /// <summary>
    /// Type of the compliance issue.
    /// </summary>
    ComplianceIssueType Type,

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    IssueSeverity Severity,

    /// <summary>
    /// Human-readable description of the issue.
    /// </summary>
    string Description,

    /// <summary>
    /// Guideline reference that this issue relates to.
    /// </summary>
    string? GuidelineReference);

/// <summary>
/// Overall compliance status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComplianceStatus
{
    /// <summary>
    /// Fully compliant with all guidelines.
    /// </summary>
    Compliant,

    /// <summary>
    /// Minor issues found that should be addressed.
    /// </summary>
    NeedsReview,

    /// <summary>
    /// Significant issues that must be resolved before submission.
    /// </summary>
    NonCompliant
}

/// <summary>
/// Types of compliance issues that can be detected.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComplianceIssueType
{
    /// <summary>
    /// Description is missing or too vague.
    /// </summary>
    VagueDescription,

    /// <summary>
    /// Description uses block billing (multiple tasks combined).
    /// </summary>
    BlockBilling,

    /// <summary>
    /// Time increment is too large or not reasonable.
    /// </summary>
    ExcessiveTimeIncrement,

    /// <summary>
    /// Category doesn't match the description.
    /// </summary>
    CategoryMismatch,

    /// <summary>
    /// Entry should likely be non-billable.
    /// </summary>
    QuestionableBillability,

    /// <summary>
    /// Duplicate or redundant entry.
    /// </summary>
    PotentialDuplicate,

    /// <summary>
    /// Administrative task billed as client work.
    /// </summary>
    AdministrativeTaskBilled,

    /// <summary>
    /// Missing required information (client, matter, etc.).
    /// </summary>
    MissingInformation,

    /// <summary>
    /// Other guideline violation.
    /// </summary>
    Other
}

/// <summary>
/// Severity level of compliance issues.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueSeverity
{
    /// <summary>
    /// Minor issue - informational only.
    /// </summary>
    Low,

    /// <summary>
    /// Medium issue - should be addressed.
    /// </summary>
    Medium,

    /// <summary>
    /// High severity - must be addressed before submission.
    /// </summary>
    High
}
