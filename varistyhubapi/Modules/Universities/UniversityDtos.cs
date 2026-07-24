namespace VarsityHub.Modules.Universities;

/// <summary>
/// A university in the catalog (read-only for students).
/// </summary>
public record University(
    Guid Id,
    string Name,
    string ShortCode,
    string Province,
    int? MinAps,
    decimal? TuitionFrom,
    int ProgrammesCount,
    int FacultiesCount
);

/// <summary>
/// Request to add a university to favorites.
/// </summary>
public record FavoriteUniversity(Guid UniversityId);
