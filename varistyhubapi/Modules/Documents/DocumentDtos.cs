namespace VarsityHub.Modules.Documents;

/// <summary>
/// Request for a signed upload URL.
/// </summary>
public record UploadUrlRequest(string Filename);

/// <summary>
/// Signed upload URL plus the storage path the client should record afterwards.
/// </summary>
public record UploadUrlResponse(string UploadUrl, string StoragePath);

/// <summary>
/// Request to record document metadata after upload.
/// </summary>
public record CreateDocumentRequest(string Name, string Type, string StoragePath, long SizeBytes);
