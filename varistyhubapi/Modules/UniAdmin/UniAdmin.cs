using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.UniAdmin;

public record UniApplicationDto(
    Guid Id, string StudentName, string ProgrammeName, string Status, int? ApsAtApply,
    DateTime CreatedAt, DateTime UpdatedAt);

public record UpdateStatus(string Status);

/// <summary>
/// University-admin operations. Runs on the service path but always constrains to the
/// universities the caller administers (public.university_admins).
/// </summary>
public sealed class UniAdminRepo(SupabaseDb db)
{
    private static readonly string[] ValidStatuses =
        ["under_review", "pending_documents", "approved", "waitlisted", "rejected", "withdrawn"];

    public Task<IEnumerable<UniApplicationDto>> GetApplicationsAsync(Guid adminId, string? status) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<UniApplicationDto>(new CommandDefinition("""
                select a.id, pr.full_name as StudentName, p.name as ProgrammeName,
                       a.status::text as Status, a.aps_at_apply as ApsAtApply,
                       a.created_at as CreatedAt, a.updated_at as UpdatedAt
                from public.applications a
                join public.programmes p on p.id = a.programme_id
                join public.profiles pr on pr.id = a.student_id
                where a.university_id in (
                    select university_id from public.university_admins where profile_id = @adminId)
                  and (@status is null or a.status::text = @status)
                order by a.created_at desc
            """, new { adminId, status }, tx)));

    /// <summary>Update status only if the application is at a university the admin manages.</summary>
    public Task<bool> UpdateStatusAsync(Guid adminId, Guid applicationId, string status) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            if (!ValidStatuses.Contains(status))
                throw new ArgumentException($"Invalid status '{status}'");

            var affected = await c.ExecuteAsync(new CommandDefinition("""
                update public.applications
                set status = @status::application_status,
                    decision_at = case when @status in ('approved','waitlisted','rejected') then now() else decision_at end,
                    updated_at = now()
                where id = @applicationId
                  and university_id in (
                      select university_id from public.university_admins where profile_id = @adminId)
            """, new { adminId, applicationId, status }, tx));
            return affected > 0;
        });
}

[ApiController]
[Route("api/uni-admin")]
[Authorize(Policy = "UniAdmin")]
public sealed class UniAdminController(UniAdminRepo repo, IUserContext me, IAuditService audit) : ControllerBase
{
    [HttpGet("applications")]
    public async Task<ActionResult<IEnumerable<UniApplicationDto>>> GetApplications([FromQuery] string? status)
        => Ok(await repo.GetApplicationsAsync(Guid.Parse(me.UserId!), status));

    [HttpPatch("applications/{id}")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatus body)
    {
        var adminId = Guid.Parse(me.UserId!);
        try
        {
            var ok = await repo.UpdateStatusAsync(adminId, id, body.Status);
            if (!ok) return NotFound(new { error = "Application not found for your university." });

            await audit.LogAsync(adminId, "application.status_changed", "application", id, new { body.Status });
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
