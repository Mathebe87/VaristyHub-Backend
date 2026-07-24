using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VarsityHub.Services;

/// <summary>
/// Server-side registration via the Supabase GoTrue Admin API using the service-role key.
/// Creates the auth user with email_confirm=false (we run our own OTP step), stashing
/// role/full_name/phone in user_metadata for the handle_new_user trigger.
/// The frontend still logs in through Supabase Auth to obtain a JWT after verification.
/// </summary>
public sealed class AuthService(HttpClient http, IConfiguration cfg, IOtpService otp)
{
    private readonly string _url = cfg["Supabase:Url"]!.TrimEnd('/');
    private readonly string _serviceKey = cfg["Supabase:ServiceRoleKey"]!;
    private readonly string _anonKey = cfg["Supabase:AnonKey"] ?? "";

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

    public async Task<Guid> RegisterAsync(RegisterCommand r)
    {
        http.DefaultRequestHeaders.Add("apikey", _serviceKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);

        var body = new
        {
            email = r.Email,
            password = r.Password,
            email_confirm = false,
            user_metadata = new { role = "student", full_name = r.FullName, phone = r.Phone }
        };

        var resp = await http.PostAsJsonAsync($"{_url}/auth/v1/admin/users", body);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GoTrue create-user failed ({(int)resp.StatusCode}): {detail}");
        }

        var created = await resp.Content.ReadFromJsonAsync<GoTrueUser>()
                      ?? throw new InvalidOperationException("GoTrue returned no user.");

        var channel = r.Channel == "sms" ? "sms" : "email";
        var destination = channel == "sms" ? r.Phone! : r.Email;
        await otp.IssueAsync(created.Id, destination, channel);

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
