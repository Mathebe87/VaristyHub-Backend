using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VarsityHub.Services;

/// <summary>
/// Server-side auth via the Supabase GoTrue Admin API using the service-role key.
/// The backend owns registration/login end to end — the frontend only sends
/// credentials and stores the returned JWT. No frontend Supabase SDK required.
/// </summary>
public sealed class AuthService(HttpClient http, IConfiguration cfg)
{
    private readonly string _url = cfg["Supabase:Url"]!.TrimEnd('/');
    private readonly string _serviceKey = cfg["Supabase:ServiceRoleKey"]!;
    private readonly string _anonKey = cfg["Supabase:AnonKey"] ?? "";
    // Auto-confirm new users so they can log in immediately (no email step).
    // Set Auth:RequireEmailConfirmation=true later to switch on verification.
    private readonly bool _autoConfirm =
        !cfg.GetValue<bool>("Auth:RequireEmailConfirmation");

    /// <summary>
    /// Log in via GoTrue's password grant and return the tokens. The backend does not mint
    /// tokens itself — this proxies Supabase Auth (handy for Swagger and non-JS clients).
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string email, string password)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/token?grant_type=password")
        {
            Content = JsonContent.Create(new { email, password })
        };
        req.Headers.Add("apikey", _anonKey);

        using var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await resp.Content.ReadFromJsonAsync<LoginResponse>()
               ?? throw new InvalidOperationException("GoTrue returned no token.");
    }

    // Self-service registration is always a student.
    public Task<Guid> RegisterAsync(RegisterCommand r) =>
        CreateUserAsync(r.FullName, r.Email, r.Phone, r.Password, "student", _autoConfirm);

    /// <summary>
    /// Create a GoTrue user with an explicit role (admin use). The handle_new_user trigger
    /// reads the role from user_metadata and provisions the matching profile row.
    /// </summary>
    public async Task<Guid> CreateUserAsync(string fullName, string email, string? phone, string password,
        string role, bool emailConfirm = true)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/admin/users")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                email_confirm = emailConfirm,
                user_metadata = new { role, full_name = fullName, phone }
            })
        };
        req.Headers.Add("apikey", _serviceKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);

        using var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GoTrue create-user failed ({(int)resp.StatusCode}): {detail}");
        }

        var created = await resp.Content.ReadFromJsonAsync<GoTrueUser>()
                      ?? throw new InvalidOperationException("GoTrue returned no user.");
        return created.Id;
    }

    /// <summary>Deactivate a user by banning them in GoTrue (long ban_duration).</summary>
    public async Task DeactivateAsync(Guid userId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{_url}/auth/v1/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { ban_duration = "876000h" }) // ~100 years
        };
        req.Headers.Add("apikey", _serviceKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);

        using var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GoTrue deactivate failed ({(int)resp.StatusCode}).");
    }
}

public sealed record RegisterCommand(string FullName, string Email, string? Phone, string Password, string Channel);

public sealed record GoTrueUser([property: JsonPropertyName("id")] Guid Id, [property: JsonPropertyName("email")] string? Email);

public sealed record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
