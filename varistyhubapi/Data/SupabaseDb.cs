using System.Data;
using System.Text.Json;
using Npgsql;

/// <summary>
/// Factory for opening Supabase connections with RLS-aware transaction handling.
/// Supports running SQL as the authenticated user (RLS enforced) or as service role (RLS bypassed).
/// </summary>
public sealed class SupabaseDb(IConfiguration cfg)
{
    private readonly string _cs = cfg.GetConnectionString("Supabase")!;

    /// <summary>
    /// Run a query as the authenticated user — RLS policies apply.
    /// Sets request.jwt.claims and role to 'authenticated' within a transaction.
    /// </summary>
    public async Task<T> AsUserAsync<T>(string? userId, string? email,
        Func<NpgsqlConnection, IDbTransaction, Task<T>> work)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Authenticated caller when we have a user id; otherwise the anon role
        // (RLS policies for anon apply — e.g. the public universities catalog).
        var authenticated = !string.IsNullOrEmpty(userId);
        var role = authenticated ? "authenticated" : "anon";

        // Build the JWT claims blob that PostgreSQL functions read via current_setting()
        var claims = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["sub"] = authenticated ? userId : null,
            ["role"] = role,
            ["email"] = authenticated ? email : null
        });

        // Set the claims and switch role — RLS will enforce. Role name can't be
        // parameterised, so it comes from a fixed allow-list above (never user input).
        await using (var cmd = new NpgsqlCommand(
            $"select set_config('request.jwt.claims', @c, true); set local role {role};", conn, tx))
        {
            cmd.Parameters.AddWithValue("c", claims);
            await cmd.ExecuteNonQueryAsync();
        }

        var result = await work(conn, tx);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>
    /// Run a query with full privileges — RLS bypassed (service role).
    /// Use only for OTP issue/verify, payment webhooks, notifications, admin jobs, and migrations.
    /// </summary>
    public async Task<T> AsServiceAsync<T>(Func<NpgsqlConnection, IDbTransaction, Task<T>> work)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var result = await work(conn, tx);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>
    /// Run a query as service role without a transaction (for read-only operations).
    /// </summary>
    public async Task<T> AsServiceReadAsync<T>(Func<NpgsqlConnection, Task<T>> work)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        return await work(conn);
    }

    /// <summary>
    /// Look up a user's role from public.profiles. Used to enrich the user_role claim when the
    /// Supabase Access Token Hook isn't configured. Returns null if not found/invalid id.
    /// </summary>
    public async Task<string?> GetUserRoleAsync(string userId)
    {
        if (!Guid.TryParse(userId, out var id)) return null;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select role::text from public.profiles where id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (await cmd.ExecuteScalarAsync()) as string;
    }
}
