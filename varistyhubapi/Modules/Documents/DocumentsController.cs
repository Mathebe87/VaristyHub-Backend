using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Documents;

/// <summary>
/// Document endpoints. Students request signed upload URLs (recommended flow)
/// and list their own documents. Metadata is tracked in public.documents.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DocumentsController(IStorageService storage, IUserContext me) : ControllerBase
{
    /// <summary>
    /// Get a signed upload URL for a file in the private 'documents' bucket.
    /// The client uploads directly to Supabase Storage, then calls POST /api/documents
    /// to record the metadata. Path convention: {userId}/{filename}.
    /// </summary>
    [HttpPost("upload-url")]
    public async Task<ActionResult<UploadUrlResponse>> GetUploadUrl([FromBody] UploadUrlRequest body)
    {
        var userId = Guid.Parse(me.UserId!);
        var url = await storage.GetUploadUrlAsync(userId, body.Filename, TimeSpan.FromHours(1));
        return Ok(new UploadUrlResponse(url, $"{userId:N}/{body.Filename}"));
    }

    /// <summary>
    /// Record document metadata after the client has uploaded the file.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateDocumentRequest body)
    {
        var userId = Guid.Parse(me.UserId!);
        var id = await storage.CreateDocumentAsync(userId, body.Name, body.Type, body.StoragePath, body.SizeBytes);
        return CreatedAtAction(nameof(GetMyDocuments), new { id }, new { id });
    }

    /// <summary>
    /// List the current student's documents.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentDetail>>> GetMyDocuments()
    {
        var userId = Guid.Parse(me.UserId!);
        var docs = await storage.GetDocumentsAsync(userId);
        return Ok(docs);
    }
}
