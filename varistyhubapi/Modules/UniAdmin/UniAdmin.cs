using System.Data;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.UniAdmin;

public record UniSummary(int Total, int Submitted, int UnderReview, int Approved, int Rejected, int PendingDocuments, int NewToday);
public record UniApplicationDto(Guid Id, string StudentName, string ProgrammeName, string Status, int? ApsAtApply, DateTime CreatedAt, DateTime UpdatedAt);
public record UniApplicationDetail(Guid Id, Guid StudentId, string ApplicantName, string? ApplicantEmail, string Programme, string University, string Status, int? ApsAtApply, int? CurrentAps, string? Notes, DateTime CreatedAt, DateTime UpdatedAt);
public record UniDocItem(Guid Id, Guid ApplicationId, string Name, string Type, string StoragePath, bool IsVerified);
public record ProgrammeAppCount(Guid ProgrammeId, string Programme, int Count);
public record UniProgrammeDto(Guid Id, string Name, string Qualification, int MinAps, Guid? FacultyId, decimal? TuitionPerYear, decimal? DurationYears, DateTime? ApplicationDeadline, bool IsActive);
public record NewProgramme(Guid? UniversityId, Guid? FacultyId, string Name, string Qualification, int MinAps, decimal? DurationYears, decimal? TuitionPerYear, string? Description, DateTime? ApplicationDeadline);
public record UpdateProgramme(string? Name, int? MinAps, decimal? TuitionPerYear, decimal? DurationYears, DateTime? ApplicationDeadline, string? Description, bool? IsActive);
public record UpdateStatus(string Status, string? Note);
public record MyUniversity(Guid Id, string Name, string ShortCode, string Province, string? Domain, string? Website);
public record UpdateMyUniversity(string? Name, string? Province, string? Domain, string? Website, string? LogoUrl);
public record FacultyDto(Guid Id, string Name, Guid UniversityId);
public record NewFaculty(string Name, Guid? UniversityId);
public record RenameFaculty(string Name);

/// <summary>
/// University-admin operations, always constrained to the universities the caller administers
/// (public.university_admins). Runs on the service path; scoping is explicit in every query.
/// </summary>
public sealed class UniAdminRepo(SupabaseDb db)
{
    private static readonly string[] ValidStatuses =
        ["under_review", "pending_documents", "approved", "waitlisted", "rejected", "withdrawn"];

    private const string MineFilter =
        "university_id in (select university_id from public.university_admins where profile_id = @adminId)";

    // Same scope, but for the universities table itself (its key column is `id`).
    private const string MineUniversityFilter =
        "id in (select university_id from public.university_admins where profile_id = @adminId)";

    public Task<UniSummary> SummaryAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryFirstAsync<UniSummary>(new CommandDefinition($"""
                select
                  count(*)::int as Total,
                  (count(*) filter (where status='submitted'))::int as Submitted,
                  (count(*) filter (where status='under_review'))::int as UnderReview,
                  (count(*) filter (where status='approved'))::int as Approved,
                  (count(*) filter (where status='rejected'))::int as Rejected,
                  (count(*) filter (where status='pending_documents'))::int as PendingDocuments,
                  (count(*) filter (where created_at::date = current_date))::int as NewToday
                from public.applications where {MineFilter}
            """, new { adminId }, tx)));

    public Task<IEnumerable<UniApplicationDto>> GetApplicationsAsync(Guid adminId, string? status, Guid? programmeId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<UniApplicationDto>(new CommandDefinition($"""
                select a.id, pr.full_name as StudentName, p.name as ProgrammeName,
                       a.status::text as Status, a.aps_at_apply as ApsAtApply,
                       a.created_at as CreatedAt, a.updated_at as UpdatedAt
                from public.applications a
                join public.programmes p on p.id = a.programme_id
                join public.profiles pr on pr.id = a.student_id
                where a.{MineFilter}
                  and (@status is null or a.status::text = @status)
                  and (@programmeId is null or a.programme_id = @programmeId)
                order by a.created_at desc
            """, new { adminId, status, programmeId }, tx)));

    public Task<UniApplicationDetail?> GetApplicationAsync(Guid adminId, Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<UniApplicationDetail>(new CommandDefinition($"""
                select a.id, a.student_id as StudentId, p.full_name as ApplicantName, p.email::text as ApplicantEmail,
                       pr.name as Programme, u.name as University, a.status::text as Status,
                       a.aps_at_apply as ApsAtApply, sa.aps as CurrentAps, a.notes,
                       a.created_at as CreatedAt, a.updated_at as UpdatedAt
                from public.applications a
                join public.profiles p on p.id = a.student_id
                join public.programmes pr on pr.id = a.programme_id
                join public.universities u on u.id = a.university_id
                left join public.student_aps sa on sa.student_id = a.student_id
                where a.id = @id and a.{MineFilter}
            """, new { adminId, id }, tx)));

    /// <summary>Returns (updated, studentId) so the caller can notify the student.</summary>
    public Task<(bool ok, Guid studentId)> UpdateStatusAsync(Guid adminId, Guid applicationId, string status, string? note) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            if (!ValidStatuses.Contains(status))
                throw new ArgumentException($"Invalid status '{status}'");

            var studentId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition($"""
                update public.applications
                set status = @status::application_status,
                    notes = coalesce(@note, notes),
                    decision_at = case when @status in ('approved','waitlisted','rejected') then now() else decision_at end,
                    updated_at = now()
                where id = @applicationId and {MineFilter}
                returning student_id
            """, new { adminId, applicationId, status, note }, tx));
            return (studentId is not null, studentId ?? Guid.Empty);
        });

    public Task<IEnumerable<UniDocItem>> GetDocumentsAsync(Guid adminId, Guid applicationId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<UniDocItem>(new CommandDefinition($"""
                select d.id, ad.application_id as ApplicationId, d.name, d.type::text as Type,
                       d.storage_path as StoragePath, d.is_verified as IsVerified
                from public.application_documents ad
                join public.documents d on d.id = ad.document_id
                join public.applications a on a.id = ad.application_id
                where ad.application_id = @applicationId and a.{MineFilter}
            """, new { adminId, applicationId }, tx)));

    public Task<bool> VerifyDocumentAsync(Guid adminId, Guid documentId) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var affected = await c.ExecuteAsync(new CommandDefinition($"""
                update public.documents set is_verified = true
                where id = @documentId and id in (
                    select ad.document_id from public.application_documents ad
                    join public.applications a on a.id = ad.application_id
                    where a.{MineFilter})
            """, new { adminId, documentId }, tx));
            return affected > 0;
        });

    public Task<IEnumerable<ProgrammeAppCount>> ProgrammeAppsAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<ProgrammeAppCount>(new CommandDefinition($"""
                select pr.id as ProgrammeId, pr.name as Programme, count(a.id)::int as Count
                from public.programmes pr
                left join public.applications a on a.programme_id = pr.id
                where pr.{MineFilter}
                group by pr.id, pr.name
                order by count(a.id) desc
            """, new { adminId }, tx)));

    public Task<IEnumerable<UniProgrammeDto>> GetProgrammesAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<UniProgrammeDto>(new CommandDefinition($"""
                select id, name, qualification::text as Qualification, min_aps as MinAps,
                       faculty_id as FacultyId, tuition_per_year as TuitionPerYear,
                       duration_years as DurationYears, application_deadline as ApplicationDeadline,
                       is_active as IsActive
                from public.programmes where {MineFilter}
                order by name
            """, new { adminId }, tx)));

    public Task<Guid> CreateProgrammeAsync(Guid adminId, NewProgramme n) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var uniId = await ResolveUniversityAsync(c, tx, adminId, n.UniversityId);
            return await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.programmes
                    (university_id, faculty_id, name, qualification, min_aps, duration_years, tuition_per_year, description, application_deadline, is_active)
                values (@uniId, @FacultyId, @Name, @Qualification::qualification_type, @MinAps,
                        @DurationYears, @TuitionPerYear, @Description, @ApplicationDeadline, true)
                returning id
            """, new { uniId, n.FacultyId, n.Name, n.Qualification, n.MinAps, n.DurationYears, n.TuitionPerYear, n.Description, n.ApplicationDeadline }, tx));
        });

    // Resolve which university a uni-admin action targets: an explicit (owned) id, or their
    // single university if they manage exactly one. Otherwise a clear error.
    private static async Task<Guid> ResolveUniversityAsync(NpgsqlConnection c, IDbTransaction tx, Guid adminId, Guid? requested)
    {
        var owned = (await c.QueryAsync<Guid>(new CommandDefinition(
            "select university_id from public.university_admins where profile_id = @adminId",
            new { adminId }, tx))).ToList();

        if (requested is Guid r)
            return owned.Contains(r) ? r : throw new UnauthorizedAccessException("Not an admin of that university.");
        if (owned.Count == 1) return owned[0];
        if (owned.Count == 0) throw new InvalidOperationException("You are not linked to any university.");
        throw new InvalidOperationException("You manage multiple universities — specify universityId.");
    }

    public Task<IEnumerable<MyUniversity>> GetMyUniversitiesAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<MyUniversity>(new CommandDefinition($"""
                select u.id, u.name, u.short_code as ShortCode, u.province, u.domain, u.website
                from public.universities u
                where u.{MineUniversityFilter}
                order by u.name
            """, new { adminId }, tx)));

    public Task<bool> UpdateUniversityAsync(Guid adminId, Guid id, UpdateMyUniversity u) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition($"""
                update public.universities
                set name = coalesce(@Name, name), province = coalesce(@Province, province),
                    domain = coalesce(@Domain, domain), website = coalesce(@Website, website),
                    logo_url = coalesce(@LogoUrl, logo_url), updated_at = now()
                where id = @id and {MineUniversityFilter}
            """, new { adminId, id, u.Name, u.Province, u.Domain, u.Website, u.LogoUrl }, tx)) > 0);

    public Task<IEnumerable<FacultyDto>> GetFacultiesAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<FacultyDto>(new CommandDefinition($"""
                select id, name, university_id as UniversityId
                from public.faculties where {MineFilter}
                order by name
            """, new { adminId }, tx)));

    public Task<Guid> CreateFacultyAsync(Guid adminId, NewFaculty n) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var uniId = await ResolveUniversityAsync(c, tx, adminId, n.UniversityId);
            return await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.faculties (university_id, name) values (@uniId, @Name)
                on conflict (university_id, name) do update set name = excluded.name
                returning id
            """, new { uniId, n.Name }, tx));
        });

    public Task<bool> RenameFacultyAsync(Guid adminId, Guid id, string name) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition($"""
                update public.faculties set name = @name where id = @id and {MineFilter}
            """, new { adminId, id, name }, tx)) > 0);

    public Task<bool> DeleteFacultyAsync(Guid adminId, Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition($"""
                delete from public.faculties where id = @id and {MineFilter}
            """, new { adminId, id }, tx)) > 0);

    public Task<bool> UpdateProgrammeAsync(Guid adminId, Guid id, UpdateProgramme u) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var affected = await c.ExecuteAsync(new CommandDefinition($"""
                update public.programmes
                set name = coalesce(@Name, name),
                    min_aps = coalesce(@MinAps, min_aps),
                    tuition_per_year = coalesce(@TuitionPerYear, tuition_per_year),
                    duration_years = coalesce(@DurationYears, duration_years),
                    application_deadline = coalesce(@ApplicationDeadline, application_deadline),
                    description = coalesce(@Description, description),
                    is_active = coalesce(@IsActive, is_active),
                    updated_at = now()
                where id = @id and {MineFilter}
            """, new { adminId, id, u.Name, u.MinAps, u.TuitionPerYear, u.DurationYears, u.ApplicationDeadline, u.Description, u.IsActive }, tx));
            return affected > 0;
        });

    public Task<IEnumerable<StatusN>> ReportsAsync(Guid adminId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<StatusN>(new CommandDefinition($"""
                select status::text as Status, sum(n)::int as N
                from public.application_funnel
                where {MineFilter}
                group by status
                order by sum(n) desc
            """, new { adminId }, tx)));
}

public record StatusN(string Status, int N);

[ApiController]
[Route("api/uni-admin")]
[Authorize(Policy = "UniAdmin")]
public sealed class UniAdminController(UniAdminRepo repo, IUserContext me, IAuditService audit, INotificationService notifications) : ControllerBase
{
    private Guid AdminId => Guid.Parse(me.UserId!);

    [HttpGet("summary")]
    public async Task<ActionResult<UniSummary>> Summary() => Ok(await repo.SummaryAsync(AdminId));

    [HttpGet("applications")]
    public async Task<ActionResult<IEnumerable<UniApplicationDto>>> GetApplications(
        [FromQuery] string? status, [FromQuery] Guid? programmeId)
        => Ok(await repo.GetApplicationsAsync(AdminId, status, programmeId));

    [HttpGet("applications/{id}")]
    public async Task<ActionResult<UniApplicationDetail>> GetApplication(Guid id)
    {
        var app = await repo.GetApplicationAsync(AdminId, id);
        return app is null ? NotFound() : Ok(app);
    }

    [HttpPatch("applications/{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatus body)
    {
        try
        {
            var (ok, studentId) = await repo.UpdateStatusAsync(AdminId, id, body.Status, body.Note);
            if (!ok) return NotFound(new { error = "Application not found for your university." });

            await audit.LogAsync(AdminId, "application.status_changed", "application", id, new { body.Status, body.Note });
            await notifications.NotifyAsync(studentId, "application",
                "Application status updated", $"Your application is now '{body.Status}'.", "/applications");
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("documents")]
    public async Task<ActionResult<IEnumerable<UniDocItem>>> Documents([FromQuery] Guid applicationId)
        => Ok(await repo.GetDocumentsAsync(AdminId, applicationId));

    [HttpPatch("documents/{id}/verify")]
    public async Task<IActionResult> VerifyDocument(Guid id)
    {
        var ok = await repo.VerifyDocumentAsync(AdminId, id);
        if (!ok) return NotFound(new { error = "Document not found for your university." });
        await audit.LogAsync(AdminId, "document.verified", "document", id);
        return NoContent();
    }

    [HttpGet("programme-apps")]
    public async Task<ActionResult<IEnumerable<ProgrammeAppCount>>> ProgrammeApps()
        => Ok(await repo.ProgrammeAppsAsync(AdminId));

    [HttpGet("programmes")]
    public async Task<ActionResult<IEnumerable<UniProgrammeDto>>> GetProgrammes()
        => Ok(await repo.GetProgrammesAsync(AdminId));

    [HttpPost("programmes")]
    public async Task<ActionResult<object>> CreateProgramme([FromBody] NewProgramme body)
    {
        try
        {
            var id = await repo.CreateProgrammeAsync(AdminId, body);
            await audit.LogAsync(AdminId, "programme.created", "programme", id, new { body.Name });
            return Ok(new { id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // Which university/universities the admin manages (frontend uses this for pickers/headers).
    [HttpGet("universities")]
    public async Task<ActionResult<IEnumerable<MyUniversity>>> MyUniversities()
        => Ok(await repo.GetMyUniversitiesAsync(AdminId));

    [HttpPatch("universities/{id}")]
    public async Task<IActionResult> UpdateUniversity(Guid id, [FromBody] UpdateMyUniversity body)
    {
        if (!await repo.UpdateUniversityAsync(AdminId, id, body))
            return NotFound(new { error = "University not found for your account." });
        await audit.LogAsync(AdminId, "university.updated", "university", id);
        return NoContent();
    }

    [HttpGet("faculties")]
    public async Task<ActionResult<IEnumerable<FacultyDto>>> Faculties()
        => Ok(await repo.GetFacultiesAsync(AdminId));

    [HttpPost("faculties")]
    public async Task<ActionResult<object>> CreateFaculty([FromBody] NewFaculty body)
    {
        try
        {
            var id = await repo.CreateFacultyAsync(AdminId, body);
            await audit.LogAsync(AdminId, "faculty.created", "faculty", id, new { body.Name });
            return Ok(new { id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("faculties/{id}")]
    public async Task<IActionResult> RenameFaculty(Guid id, [FromBody] RenameFaculty body)
    {
        if (!await repo.RenameFacultyAsync(AdminId, id, body.Name))
            return NotFound(new { error = "Faculty not found for your university." });
        await audit.LogAsync(AdminId, "faculty.updated", "faculty", id);
        return NoContent();
    }

    [HttpDelete("faculties/{id}")]
    public async Task<IActionResult> DeleteFaculty(Guid id)
    {
        if (!await repo.DeleteFacultyAsync(AdminId, id))
            return NotFound(new { error = "Faculty not found for your university." });
        await audit.LogAsync(AdminId, "faculty.deleted", "faculty", id);
        return NoContent();
    }

    [HttpPatch("programmes/{id}")]
    public async Task<IActionResult> UpdateProgramme(Guid id, [FromBody] UpdateProgramme body)
    {
        var ok = await repo.UpdateProgrammeAsync(AdminId, id, body);
        if (!ok) return NotFound(new { error = "Programme not found for your university." });
        await audit.LogAsync(AdminId, "programme.updated", "programme", id);
        return NoContent();
    }

    [HttpGet("reports")]
    public async Task<ActionResult<IEnumerable<StatusN>>> Reports() => Ok(await repo.ReportsAsync(AdminId));
}
