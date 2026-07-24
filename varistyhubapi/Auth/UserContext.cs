/// <summary>
/// Represents the current authenticated user extracted from the JWT token.
/// Resolved per-request from the HTTP context.
/// </summary>
public interface IUserContext
{
    string? UserId { get; }
    string? Email { get; }
}

/// <summary>
/// Implementation of IUserContext capturing user identity from JWT claims.
/// </summary>
public sealed record UserContext(string? UserId, string? Email) : IUserContext;
