namespace VarsityHub.Services;

/// <summary>
/// File storage service interface for Supabase Storage.
/// Supports signed URL generation and metadata tracking.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Generate a signed URL for the user to upload a file to the 'documents' bucket.
    /// Path format: {userId}/{filename}
    /// </summary>
    Task<string> GetUploadUrlAsync(Guid userId, string filename, TimeSpan expiresIn);

    /// <summary>
    /// Generate a signed URL to download/view a file (for public/private buckets).
    /// </summary>
    Task<string> GetDownloadUrlAsync(string bucketName, string path, TimeSpan expiresIn);

    /// <summary>
    /// Delete a file from storage (admin/owner only).
    /// </summary>
    Task DeleteAsync(string bucketName, string path);

    /// <summary>
    /// Upload a file via the backend (proxy mode).
    /// Used when direct upload URLs aren't available.
    /// </summary>
    Task<string> UploadAsync(string bucketName, string path, Stream content);

    /// <summary>
    /// Record document metadata in the database after a file is uploaded.
    /// </summary>
    Task<Guid> CreateDocumentAsync(Guid studentId, string name, string type, string storagePath, long sizeBytes);

    /// <summary>
    /// Get all documents for a student.
    /// </summary>
    Task<IEnumerable<DocumentDetail>> GetDocumentsAsync(Guid studentId);
}

/// <summary>
/// Document metadata stored in the database.
/// </summary>
public record DocumentDetail(
    Guid Id,
    Guid StudentId,
    string Name,
    string Type,  // resume, transcript, etc
    string StoragePath,
    long? SizeBytes,
    DateTime CreatedAt
);
