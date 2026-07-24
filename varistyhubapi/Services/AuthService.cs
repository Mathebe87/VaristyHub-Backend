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
}

public sealed record RegisterCommand(string FullName, string Email, string? Phone, string Password, string Channel);

public sealed record GoTrueUser([property: JsonPropertyName("id")] Guid Id, [property: JsonPropertyName("email")] string? Email);
