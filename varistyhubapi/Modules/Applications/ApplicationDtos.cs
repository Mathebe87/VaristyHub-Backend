namespace VarsityHub.Modules.Applications;

/// <summary>
/// Request to create a new application.
/// </summary>
public record NewApplication(Guid ProgrammeId, Guid UniversityId);

/// <summary>
/// An application submitted by a student.
/// </summary>
public record ApplicationDetail(
    Guid Id,
    Guid StudentId,
    Guid UniversityId,
    Guid ProgrammeId,
    string Status,  // submitted, reviewing, accepted, rejected
    int? ApsAtApply,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Summary of applications for a student.
/// </summary>
public record ApplicationSummary(
    Guid Id,
    string UniversityName,
    string ProgrammeName,
    string Status,
    DateTime CreatedAt
);

/// <summary>
/// Request to update application status (admin/uni-admin only).
/// </summary>
public record UpdateApplicationStatus(string Status);
