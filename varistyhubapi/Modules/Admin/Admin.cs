using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Admin;

public record AdminSummary(int Students, int Counsellors, int Parents, int UniAdmins, int SuperAdmins, int Applications, int Universities, decimal Revenue);
public record AdminUser(Guid Id, string Role, string FullName, string? Email, string? Phone, DateTime CreatedAt);
public record SetRole(string Role);
public record CreateUserRequest(
    string FullName, string Email, string Password, string Role,
    string? Phone, Guid? UniversityId, Guid? StudentId, string? Relationship, string? Title);
public record AdminUniversity(Guid Id, string Name, string ShortCode, string Province, string? Domain, string? Website, bool IsVerified);
public record NewUniversity(string Name, string ShortCode, string Province, string? Domain, string? Website);
public record UpdateUniversity(string? Name, string? Province, string? Domain, string? Website, bool? IsVerified);
public record AssignAdmin(Guid ProfileId, string? Title);
public record AdminProgramme(Guid Id, Guid UniversityId, string Name, string Qualification, int MinAps, bool IsActive);
public record NewAdminProgramme(Guid UniversityId, Guid? FacultyId, string Name, string Qualification, int MinAps, decimal? DurationYears, decimal? TuitionPerYear, string? Description, DateTime? ApplicationDeadline);
public record UpdateAdminProgramme(string? Name, int? MinAps, bool? IsActive);
public record ApsRuleDto(Guid Id, Guid? UniversityId, string Name, string? Description, string Config, bool IsActive);
public record NewApsRule(Guid? UniversityId, string Name, string? Description, string? Config);
public record UpdateApsRule(string? Name, string? Description, string? Config, bool? IsActive);
public record AdminApplication(Guid Id, string Student, string University, string Programme, string Status, DateTime CreatedAt);
public record SettingDto(string Key, string Value);
public record PutSetting(string Value);
public record AuditLogDto(Guid Id, Guid? ActorId, string Action, string? EntityType, Guid? EntityId, string Metadata, DateTime CreatedAt);
public record Broadcast(string Category, string Title, string? Body, string? ActionUrl, string? Role);
public record LinkRequest(string Type, Guid AdultId, Guid StudentId, string? Relationship);

/// <summary>
/// Super-admin platform operations. Runs on the service path (RLS bypassed); the controller's
/// Admin policy is the gate. Every mutation writes an audit_logs row via the controller.
/// </summary>
public sealed class AdminRepo(SupabaseDb db)
{
    public Task<AdminSummary> SummaryAsync() =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryFirstAsync<AdminSummary>(new CommandDefinition("""
                select
                  (count(*) filter (where role='student'))::int as Students,
                  (count(*) filter (where role='counsellor'))::int as Counsellors,
                  (count(*) filter (where role='parent'))::int as Parents,
                  (count(*) filter (where role='university_admin'))::int as UniAdmins,
                  (count(*) filter (where role='super_admin'))::int as SuperAdmins,
                  (select count(*)::int from public.applications) as Applications,
                  (select count(*)::int from public.universities) as Universities,
                  (select coalesce(sum(amount),0) from public.payments where status='paid') as Revenue
                from public.profiles
            """, transaction: tx)));

    public Task<IEnumerable<AdminUser>> UsersAsync(string? role, string? q, int limit, int offset) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminUser>(new CommandDefinition("""
                select id, role::text as Role, full_name as FullName, email::text as Email, phone, created_at as CreatedAt
                from public.profiles
                where (@role is null or role::text = @role)
                  and (@q is null or full_name ilike '%' || @q || '%')
                order by created_at desc
                limit @limit offset @offset
            """, new { role, q, limit, offset }, tx)));

    public Task SetRoleAsync(Guid userId, string role) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                "update public.profiles set role = @role::user_role, updated_at = now() where id = @userId",
                new { userId, role }, tx));
            if (role == "student")
                await c.ExecuteAsync(new CommandDefinition(
                    "insert into public.students(id) values(@userId) on conflict do nothing", new { userId }, tx));
            return 0;
        });

    // Universities CRUD
    public Task<IEnumerable<AdminUniversity>> UniversitiesAsync() =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminUniversity>(new CommandDefinition("""
                select id, name, short_code as ShortCode, province, domain, website, is_verified as IsVerified
                from public.universities order by name
            """, transaction: tx)));

    public Task<Guid> CreateUniversityAsync(NewUniversity u) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.universities (name, short_code, province, domain, website, is_verified)
                values (@Name, @ShortCode, @Province, @Domain, @Website, true)
                returning id
            """, u, tx)));

    public Task<bool> UpdateUniversityAsync(Guid id, UpdateUniversity u) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.universities
                set name = coalesce(@Name, name), province = coalesce(@Province, province),
                    domain = coalesce(@Domain, domain), website = coalesce(@Website, website),
                    is_verified = coalesce(@IsVerified, is_verified), updated_at = now()
                where id = @id
            """, new { id, u.Name, u.Province, u.Domain, u.Website, u.IsVerified }, tx)) > 0);

    public Task<bool> DeleteUniversityAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.universities where id = @id", new { id }, tx)) > 0);

    public Task AssignAdminAsync(Guid universityId, Guid profileId, string? title) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.university_admins (profile_id, university_id, title)
                values (@profileId, @universityId, @title)
                on conflict (profile_id, university_id) do update set title = excluded.title
            """, new { profileId, universityId, title }, tx));
            return 0;
        });

    // Programmes CRUD (unscoped)
    public Task<IEnumerable<AdminProgramme>> ProgrammesAsync(Guid? universityId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminProgramme>(new CommandDefinition("""
                select id, university_id as UniversityId, name, qualification::text as Qualification,
                       min_aps as MinAps, is_active as IsActive
                from public.programmes
                where (@universityId is null or university_id = @universityId)
                order by name
            """, new { universityId }, tx)));

    public Task<Guid> CreateProgrammeAsync(NewAdminProgramme n) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.programmes
                    (university_id, faculty_id, name, qualification, min_aps, duration_years, tuition_per_year, description, application_deadline, is_active)
                values (@UniversityId, @FacultyId, @Name, @Qualification::qualification_type, @MinAps,
                        @DurationYears, @TuitionPerYear, @Description, @ApplicationDeadline, true)
                returning id
            """, n, tx)));

    public Task<bool> UpdateProgrammeAsync(Guid id, UpdateAdminProgramme u) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.programmes
                set name = coalesce(@Name, name), min_aps = coalesce(@MinAps, min_aps),
                    is_active = coalesce(@IsActive, is_active), updated_at = now()
                where id = @id
            """, new { id, u.Name, u.MinAps, u.IsActive }, tx)) > 0);

    public Task<bool> DeleteProgrammeAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.programmes where id = @id", new { id }, tx)) > 0);

    // APS rules
    public Task<IEnumerable<ApsRuleDto>> ApsRulesAsync() =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<ApsRuleDto>(new CommandDefinition("""
                select id, university_id as UniversityId, name, description, config::text as Config, is_active as IsActive
                from public.aps_rules order by name
            """, transaction: tx)));

    public Task<Guid> CreateApsRuleAsync(NewApsRule r) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.aps_rules (university_id, name, description, config, is_active)
                values (@UniversityId, @Name, @Description, coalesce(@Config::jsonb, '{}'::jsonb), true)
                returning id
            """, new { r.UniversityId, r.Name, r.Description, r.Config }, tx)));

    public Task<bool> UpdateApsRuleAsync(Guid id, UpdateApsRule u) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.aps_rules
                set name = coalesce(@Name, name), description = coalesce(@Description, description),
                    config = coalesce(@Config::jsonb, config), is_active = coalesce(@IsActive, is_active),
                    updated_at = now()
                where id = @id
            """, new { id, u.Name, u.Description, u.Config, u.IsActive }, tx)) > 0);

    public Task<IEnumerable<AdminApplication>> ApplicationsAsync(string? status, int limit, int offset) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminApplication>(new CommandDefinition("""
                select a.id, p.full_name as Student, u.name as University, pr.name as Programme,
                       a.status::text as Status, a.created_at as CreatedAt
                from public.applications a
                join public.profiles p on p.id = a.student_id
                join public.universities u on u.id = a.university_id
                join public.programmes pr on pr.id = a.programme_id
                where (@status is null or a.status::text = @status)
                order by a.created_at desc
                limit @limit offset @offset
            """, new { status, limit, offset }, tx)));

    // Settings
    public Task<IEnumerable<SettingDto>> SettingsAsync() =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<SettingDto>(new CommandDefinition(
                "select key, value::text as Value from public.app_settings order by key", transaction: tx)));

    public Task PutSettingAsync(string key, string value, Guid actorId) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.app_settings (key, value, updated_by, updated_at)
                values (@key, @value::jsonb, @actorId, now())
                on conflict (key) do update set value = excluded.value, updated_by = @actorId, updated_at = now()
            """, new { key, value, actorId }, tx));
            return 0;
        });

    public Task<IEnumerable<AuditLogDto>> AuditLogsAsync(string? entity, Guid? actor, int limit) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AuditLogDto>(new CommandDefinition("""
                select id, actor_id as ActorId, action, entity_type as EntityType, entity_id as EntityId,
                       metadata::text as Metadata, created_at as CreatedAt
                from public.audit_logs
                where (@entity is null or entity_type = @entity)
                  and (@actor is null or actor_id = @actor)
                order by created_at desc
                limit @limit
            """, new { entity, actor, limit }, tx)));

    public Task<int> BroadcastAsync(Broadcast b) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.notifications (user_id, category, title, body, action_url)
                select id, @Category::notification_category, @Title, @Body, @ActionUrl
                from public.profiles
                where (@Role is null or role::text = @Role)
            """, b, tx)));

    public Task LinkAsync(LinkRequest r) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            if (r.Type == "counsellor")
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into public.counsellor_students (counsellor_id, student_id)
                    values (@AdultId, @StudentId) on conflict do nothing
                """, r, tx));
            else if (r.Type == "parent")
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into public.parent_students (parent_id, student_id, relationship)
                    values (@AdultId, @StudentId, coalesce(@Relationship, 'guardian')) on conflict do nothing
                """, r, tx));
            else
                throw new ArgumentException("Type must be 'counsellor' or 'parent'.");
            return 0;
        });
}

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminController(AdminRepo repo, IAuditService audit, AuthService authService, IUserContext me) : ControllerBase
{
    private Guid ActorId => Guid.Parse(me.UserId!);

    [HttpGet("summary")]
    public async Task<ActionResult<AdminSummary>> Summary() => Ok(await repo.SummaryAsync());

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<AdminUser>>> Users(
        [FromQuery] string? role, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await repo.UsersAsync(role, q, Math.Min(pageSize, 200), (Math.Max(1, page) - 1) * pageSize));

    private static readonly string[] Roles =
        ["student", "counsellor", "parent", "university_admin", "super_admin"];

    // Create a user with a role (and optionally link them) in one call — for the admin console.
    [HttpPost("users")]
    public async Task<ActionResult<object>> CreateUser([FromBody] CreateUserRequest body)
    {
        if (!Roles.Contains(body.Role))
            return BadRequest(new { error = $"Invalid role '{body.Role}'." });

        try
        {
            var id = await authService.CreateUserAsync(body.FullName, body.Email, body.Phone, body.Password, body.Role);

            // Optional linking based on role.
            if (body.Role == "university_admin" && body.UniversityId is Guid uni)
                await repo.AssignAdminAsync(uni, id, body.Title);
            else if (body.Role == "parent" && body.StudentId is Guid psid)
                await repo.LinkAsync(new LinkRequest("parent", id, psid, body.Relationship));
            else if (body.Role == "counsellor" && body.StudentId is Guid csid)
                await repo.LinkAsync(new LinkRequest("counsellor", id, csid, null));

            await audit.LogAsync(ActorId, "user.created", "user", id, new { body.Role, body.Email });
            return Ok(new { id });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("users/{id}/role")]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] SetRole body)
    {
        await repo.SetRoleAsync(id, body.Role);
        await audit.LogAsync(ActorId, "user.role_changed", "user", id, new { body.Role });
        return NoContent();
    }

    [HttpPost("users/{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await authService.DeactivateAsync(id);
        await audit.LogAsync(ActorId, "user.deactivated", "user", id);
        return NoContent();
    }

    [HttpGet("universities")]
    public async Task<ActionResult<IEnumerable<AdminUniversity>>> Universities() => Ok(await repo.UniversitiesAsync());

    [HttpPost("universities")]
    public async Task<ActionResult<object>> CreateUniversity([FromBody] NewUniversity body)
    {
        var id = await repo.CreateUniversityAsync(body);
        await audit.LogAsync(ActorId, "university.created", "university", id, new { body.Name });
        return Ok(new { id });
    }

    [HttpPatch("universities/{id}")]
    public async Task<IActionResult> UpdateUniversity(Guid id, [FromBody] UpdateUniversity body)
    {
        if (!await repo.UpdateUniversityAsync(id, body)) return NotFound();
        await audit.LogAsync(ActorId, "university.updated", "university", id);
        return NoContent();
    }

    [HttpDelete("universities/{id}")]
    public async Task<IActionResult> DeleteUniversity(Guid id)
    {
        if (!await repo.DeleteUniversityAsync(id)) return NotFound();
        await audit.LogAsync(ActorId, "university.deleted", "university", id);
        return NoContent();
    }

    [HttpPost("universities/{id}/admins")]
    public async Task<IActionResult> AssignAdmin(Guid id, [FromBody] AssignAdmin body)
    {
        await repo.AssignAdminAsync(id, body.ProfileId, body.Title);
        await audit.LogAsync(ActorId, "university.admin_assigned", "university", id, new { body.ProfileId });
        return NoContent();
    }

    [HttpGet("programmes")]
    public async Task<ActionResult<IEnumerable<AdminProgramme>>> Programmes([FromQuery] Guid? universityId)
        => Ok(await repo.ProgrammesAsync(universityId));

    [HttpPost("programmes")]
    public async Task<ActionResult<object>> CreateProgramme([FromBody] NewAdminProgramme body)
    {
        var id = await repo.CreateProgrammeAsync(body);
        await audit.LogAsync(ActorId, "programme.created", "programme", id, new { body.Name });
        return Ok(new { id });
    }

    [HttpPatch("programmes/{id}")]
    public async Task<IActionResult> UpdateProgramme(Guid id, [FromBody] UpdateAdminProgramme body)
    {
        if (!await repo.UpdateProgrammeAsync(id, body)) return NotFound();
        await audit.LogAsync(ActorId, "programme.updated", "programme", id);
        return NoContent();
    }

    [HttpDelete("programmes/{id}")]
    public async Task<IActionResult> DeleteProgramme(Guid id)
    {
        if (!await repo.DeleteProgrammeAsync(id)) return NotFound();
        await audit.LogAsync(ActorId, "programme.deleted", "programme", id);
        return NoContent();
    }

    [HttpGet("aps-rules")]
    public async Task<ActionResult<IEnumerable<ApsRuleDto>>> ApsRules() => Ok(await repo.ApsRulesAsync());

    [HttpPost("aps-rules")]
    public async Task<ActionResult<object>> CreateApsRule([FromBody] NewApsRule body)
    {
        var id = await repo.CreateApsRuleAsync(body);
        await audit.LogAsync(ActorId, "aps_rule.created", "aps_rule", id, new { body.Name });
        return Ok(new { id });
    }

    [HttpPatch("aps-rules/{id}")]
    public async Task<IActionResult> UpdateApsRule(Guid id, [FromBody] UpdateApsRule body)
    {
        if (!await repo.UpdateApsRuleAsync(id, body)) return NotFound();
        await audit.LogAsync(ActorId, "aps_rule.updated", "aps_rule", id);
        return NoContent();
    }

    [HttpGet("applications")]
    public async Task<ActionResult<IEnumerable<AdminApplication>>> Applications(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await repo.ApplicationsAsync(status, Math.Min(pageSize, 200), (Math.Max(1, page) - 1) * pageSize));

    [HttpGet("reports")]
    public async Task<ActionResult<AdminSummary>> Reports() => Ok(await repo.SummaryAsync());

    [HttpGet("settings")]
    public async Task<ActionResult<IEnumerable<SettingDto>>> Settings() => Ok(await repo.SettingsAsync());

    [HttpPut("settings/{key}")]
    public async Task<IActionResult> PutSetting(string key, [FromBody] PutSetting body)
    {
        await repo.PutSettingAsync(key, body.Value, ActorId);
        await audit.LogAsync(ActorId, "setting.updated", "app_setting", null, new { key });
        return NoContent();
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> AuditLogs(
        [FromQuery] string? entity, [FromQuery] Guid? actor, [FromQuery] int limit = 200)
        => Ok(await repo.AuditLogsAsync(entity, actor, Math.Min(limit, 500)));

    [HttpPost("notifications/broadcast")]
    public async Task<ActionResult<object>> BroadcastNotification([FromBody] Broadcast body)
    {
        var count = await repo.BroadcastAsync(body);
        await audit.LogAsync(ActorId, "notification.broadcast", "notification", null, new { body.Title, count });
        return Ok(new { delivered = count });
    }

    [HttpPost("links")]
    public async Task<IActionResult> Link([FromBody] LinkRequest body)
    {
        try
        {
            await repo.LinkAsync(body);
            await audit.LogAsync(ActorId, "link.created", body.Type, body.StudentId, new { body.AdultId });
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
