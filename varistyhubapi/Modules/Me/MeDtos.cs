namespace VarsityHub.Modules.Me;

public record MeProfile(
    Guid Id, string Role, string FullName, string? Email, string? Phone, string? AvatarUrl,
    bool EmailVerified, bool PhoneVerified,
    string? StudentType, string? Province, string? SchoolName, string? Grade);

public record UpdateProfile(string? FullName, string? Phone, string? AvatarUrl,
    string? Province, string? SchoolName, string? Grade);

public record UserSettingsDto(string NotificationPrefs, string Theme, string Locale);

public record UpdateSettings(string? NotificationPrefs, string? Theme, string? Locale);

public record StudentResultDto(Guid Id, string SubjectName, int Level, int Percentage, bool IsLifeOrientation);

public record ResultInput(string SubjectName, int Level, int Percentage, bool IsLifeOrientation);

public record EligibleProgramme(Guid Id, string Name, int MinAps, string University, string ShortCode);
