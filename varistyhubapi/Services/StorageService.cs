using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Implementation of storage service using the Supabase Storage REST API.
/// Generates signed URLs for client-side uploads (prevents a server bottleneck)
/// and can proxy-upload when needed. Authenticates with the service-role key.
/// </summary>
public sealed class StorageService : IStorageService
{
    private readonly SupabaseDb _db;
    private readonly HttpClient _http;
    private readonly string _supabaseUrl;

    public StorageService(SupabaseDb db, HttpClient http, IConfiguration cfg)
    {
        _db = db;
        _http = http;
        _supabaseUrl = cfg["Supabase:Url"]!.TrimEnd('/');
        var serviceRoleKey = cfg["Supabase:ServiceRoleKey"]!;

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        _http.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
    }

    /// <summary>
    /// Generate a signed upload URL for the user to upload a file to the 'documents' bucket.
    /// Path follows the RLS convention: {userId}/{filename}.
    /// </summary>
    public async Task<string> GetUploadUrlAsync(Guid userId, string filename, TimeSpan expiresIn)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);

        var path = $"{userId:N}/{filename}";

        // POST /storage/v1/object/upload/sign/{bucket}/{path} → { url: "/object/upload/sign/..." }
        using var resp = await _http.PostAsync(
            $"{_supabaseUrl}/storage/v1/object/upload/sign/documents/{path}",
            content: null);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<SignedUrlResponse>();
        return $"{_supabaseUrl}/storage/v1{payload!.Url}";
    }

    /// <summary>
    /// Generate a signed URL to download/view a file from a (private) bucket.
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(string bucketName, string path, TimeSpan expiresIn)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var expiresInSeconds = (int)expiresIn.TotalSeconds;

        // POST /storage/v1/object/sign/{bucket}/{path}  body: { expiresIn }
        using var resp = await _http.PostAsJsonAsync(
            $"{_supabaseUrl}/storage/v1/object/sign/{bucketName}/{path}",
            new { expiresIn = expiresInSeconds });
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<SignedUrlResponse>();
        return $"{_supabaseUrl}/storage/v1{payload!.Url}";
    }

    /// <summary>
    /// Delete a file from storage. Uses the service-role key (full access).
    /// </summary>
    public async Task DeleteAsync(string bucketName, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var resp = await _http.DeleteAsync(
            $"{_supabaseUrl}/storage/v1/object/{bucketName}/{path}");
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Upload a file via the backend (proxy mode). Streams the content to Supabase Storage.
    /// Returns the object's public URL path.
    /// </summary>
    public async Task<string> UploadAsync(string bucketName, string path, Stream content)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_supabaseUrl}/storage/v1/object/{bucketName}/{path}") { Content = body };
        req.Headers.Add("x-upsert", "true");

        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        return $"{_supabaseUrl}/storage/v1/object/public/{bucketName}/{path}";
    }

    /// <summary>
    /// Create a document metadata record in the database (called after upload).
    /// </summary>
    public async Task<Guid> CreateDocumentAsync(Guid studentId, string name, string type,
        string storagePath, long sizeBytes)
    {
        return await _db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.documents (student_id, name, type, storage_path, size_bytes)
                values (@studentId, @name, @type::document_type, @storagePath, @sizeBytes)
                returning id
            """, new { studentId, name, type, storagePath, sizeBytes }, tx)));
    }

    /// <summary>
    /// Get all documents for a student.
    /// </summary>
    public async Task<IEnumerable<DocumentDetail>> GetDocumentsAsync(Guid studentId)
    {
        return await _db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<DocumentDetail>(new CommandDefinition("""
                select id, student_id as StudentId, name, type, storage_path as StoragePath,
                       size_bytes as SizeBytes, uploaded_at as CreatedAt
                from public.documents
                where student_id = @studentId
                order by created_at desc
            """, new { studentId }, tx)));
    }

    private sealed record SignedUrlResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url);
}
