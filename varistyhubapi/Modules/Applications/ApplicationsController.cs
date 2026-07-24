using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Applications;

/// <summary>
/// Applications endpoints for students (create, view, upload documents).
/// Uni-admin can view and update status.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ApplicationsController(
    ApplicationRepo repo,
    IStorageService storage,
    IUserContext me) : ControllerBase
{
    /// <summary>
    /// Create a new application for a programme at a university.
    /// Requires: paid application fee, authenticated user.
    /// Fails with 409 Conflict if fee not paid.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] NewApplication body)
    {
        try
        {
            var id = await repo.CreateAsync(body);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get all applications for the current student.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationSummary>>> GetMyApplications()
    {
        var apps = await repo.GetMyApplicationsAsync();
        return Ok(apps);
    }

    /// <summary>
    /// Get details of a specific application (by ID).
    /// Student can only see their own applications (RLS).
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApplicationDetail>> GetById(Guid id)
    {
        var app = await repo.GetByIdAsync(id);
        if (app is null)
            return NotFound();
        return Ok(app);
    }

    /// <summary>
    /// Update application status (admin or uni-admin with matching university).
    /// Allowed statuses: submitted, reviewing, accepted, rejected.
    /// </summary>
    [HttpPatch("{id}")]
    [Authorize(Policy = "UniAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateApplicationStatus body)
    {
        try
        {
            await repo.UpdateStatusAsync(id, body.Status);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Upload a supporting document (resume, transcript, etc) to an application.
    /// Receives a file and associates it with the application.
    /// </summary>
    [HttpPost("{id}/documents")]
    public async Task<ActionResult<object>> UploadDocument(Guid id, IFormFile file, [FromForm] string type = "other")
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File is required" });

        try
        {
            var userId = Guid.Parse(me.UserId!);

            // Storage path convention required by the RLS policies: {userId}/{filename}
            var storagePath = $"{userId:N}/{file.FileName}";
            await using var stream = file.OpenReadStream();
            await storage.UploadAsync("documents", storagePath, stream);

            // Record the document, then link it to the application (ownership checked in repo).
            var documentId = await storage.CreateDocumentAsync(
                userId, file.FileName, type, storagePath, file.Length);
            await repo.AddDocumentAsync(id, documentId);

            return Ok(new { documentId, message = "Document uploaded successfully" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
