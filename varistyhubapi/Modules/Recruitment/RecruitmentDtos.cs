namespace VarsityHub.Modules.Recruitment;

// ---- Jobs (result DTO has an array column -> init-property record for Dapper) ----
public record AdminJob
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Location { get; init; }
    public string? SalaryText { get; init; }
    public string? Description { get; init; }
    public string[] Tags { get; init; } = [];
    public bool IsRemote { get; init; }
    public DateTime? ClosesOn { get; init; }
    public bool IsActive { get; init; }
    public int ApplicantCount { get; init; }
}

public record NewJob(string Title, string? Company, string Type, string? Location, string? SalaryText,
    string? Description, string[]? Tags, bool? IsRemote, DateTime? ClosesOn);

public record UpdateJob(string? Title, string? Company, string? Type, string? Location, string? SalaryText,
    string? Description, string[]? Tags, bool? IsRemote, DateTime? ClosesOn, bool? IsActive);

public record JobApplicant(Guid Id, Guid StudentId, string StudentName, string? StudentEmail,
    Guid? CvDocumentId, string Status, DateTime AppliedAt);

public record UpdateJobAppStatus(string Status);

// ---- Bursaries (Covers array -> init-property record) ----
public record AdminBursary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Field { get; init; } = "";
    public string? AmountText { get; init; }
    public string[] Covers { get; init; } = [];
    public int? MinAps { get; init; }
    public string? Description { get; init; }
    public DateTime? ClosesOn { get; init; }
    public bool IsActive { get; init; }
    public int ApplicantCount { get; init; }
}

public record NewBursary(string Name, string Provider, string Field, string? AmountText,
    string[]? Covers, int? MinAps, string? Description, DateTime? ClosesOn);

public record UpdateBursary(string? Name, string? Provider, string? Field, string? AmountText,
    string[]? Covers, int? MinAps, string? Description, DateTime? ClosesOn, bool? IsActive);

public record BursaryApplicant(Guid Id, Guid StudentId, string StudentName, string? StudentEmail,
    string Status, DateTime SubmittedAt);

public record UpdateBursaryAppStatus(string Status);

// ---- Employer ----
public record EmployerProfile(Guid Id, string CompanyName, string? Website, string? LogoUrl, bool IsVerified);
public record UpdateEmployerProfile(string? CompanyName, string? Website, string? LogoUrl);
