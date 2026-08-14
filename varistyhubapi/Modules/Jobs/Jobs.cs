using System.Data;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Jobs;

// Init-property record so Dapper can map the text[] Tags column (see BursaryDto note).
public record JobDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Location { get; init; }
    public string? SalaryText { get; init; }
    public string? Description { get; init; }
    public string[] Tags { get; init; } = [];
    public bool IsRemote { get; init; }
    public DateTime? ClosesOn { get; init; }
}

public record ApplyJob(Guid? CvDocumentId);

public sealed class JobRepo(SupabaseDb db, IUserContext me, INotificationService notify)
{
    public Task<IEnumerable<JobDto>> ListAsync(string? type, bool? remote, string? q) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<JobDto>(new CommandDefinition("""
                select id, title, company, type::text as Type, location, salary_text as SalaryText,
                       description, tags as Tags, is_remote as IsRemote, closes_on as ClosesOn
                from public.jobs
                where is_active
                  and (@type is null or type::text = @type)
                  and (@remote is null or is_remote = @remote)
                  and (@q is null or title ilike '%' || @q || '%' or company ilike '%' || @q || '%')
                order by coalesce(closes_on, 'infinity'::date), created_at desc
            """, new { type, remote, q }, tx)));

    public async Task<Guid> ApplyAsync(Guid jobId, Guid? cvDocumentId)
    {
        Guid appId; Guid? postedBy; string? title;
        try
        {
            (appId, postedBy, title) = await db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            {
                var id = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                    insert into public.job_applications (job_id, student_id, cv_document_id, status)
                    values (@jobId, auth.uid(), @cvDocumentId, 'applied')
                    returning id
                """, new { jobId, cvDocumentId }, tx));
                var info = await c.QueryFirstOrDefaultAsync<(Guid? PostedBy, string? Title)>(new CommandDefinition(
                    "select posted_by, title from public.jobs where id = @jobId", new { jobId }, tx));
                return (id, info.PostedBy, info.Title);
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException("You have already applied to this job.");
        }

        // Notify the employer/admin who posted the job (service path).
        if (postedBy is Guid pb)
            await notify.NotifyAsync(pb, "job", "New job applicant",
                $"A student applied to '{title}'.", "/employer-applicants");
        return appId;
    }

    public Task SaveAsync(Guid jobId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.saved_jobs (job_id, student_id)
                values (@jobId, auth.uid()) on conflict do nothing
            """, new { jobId }, tx));
            return 0;
        });

    public Task UnsaveAsync(Guid jobId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.saved_jobs where job_id = @jobId and student_id = auth.uid()",
                new { jobId }, tx));
            return 0;
        });
}

[ApiController]
[Route("api/[controller]")]
public sealed class JobsController(JobRepo repo, RecommendationService recs) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<JobDto>>> List(
        [FromQuery] string? type, [FromQuery] bool? remote, [FromQuery] string? q)
        => Ok(await repo.ListAsync(type, remote, q));

    [HttpGet("recommended")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<RankedItem>>> Recommended()
        => Ok(await recs.RecommendJobsAsync());

    [HttpPost("{id}/apply")]
    [Authorize]
    public async Task<ActionResult<object>> Apply(Guid id, [FromBody] ApplyJob? body)
        => Ok(new { id = await repo.ApplyAsync(id, body?.CvDocumentId) });

    [HttpPost("{id}/save")]
    [Authorize]
    public async Task<IActionResult> Save(Guid id)
    {
        await repo.SaveAsync(id);
        return NoContent();
    }

    [HttpDelete("{id}/save")]
    [Authorize]
    public async Task<IActionResult> Unsave(Guid id)
    {
        await repo.UnsaveAsync(id);
        return NoContent();
    }
}
