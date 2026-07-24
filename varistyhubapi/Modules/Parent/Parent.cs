using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Parent;

public record ParentSummary(int Children, int ApplicationsInProgress, int UpcomingDeadlines, int Unread);
public record ChildItem(Guid Id, string FullName, int? Aps, string? SchoolName, string? Grade, string? Relationship);
public record ChildDetail(Guid Id, string FullName, string? Email, string? Province, string? SchoolName, string? Grade, int? Aps);
public record ChildApplication(Guid Id, string University, string Programme, string Status, DateTime CreatedAt);
public record ProgrammeChoice(Guid ApplicationId, string University, string Programme, string Status);
public record DeadlineItem(string Kind, string Title, DateTime Due);

/// <summary>
/// Parent/guardian read-only views of their own children. Scoped via public.parent_students
/// and RLS (is_parent_of / can_view_student).
/// </summary>
public sealed class ParentRepo(SupabaseDb db, IUserContext me)
{
    public Task<ParentSummary> SummaryAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstAsync<ParentSummary>(new CommandDefinition("""
                select
                  (select count(*)::int from public.parent_students where parent_id = auth.uid()) as Children,
                  (select count(*)::int from public.applications a
                     where a.student_id in (select student_id from public.parent_students where parent_id = auth.uid())
                       and a.status not in ('approved','rejected')) as ApplicationsInProgress,
                  (select count(*)::int from public.applications a
                     join public.programmes p on p.id = a.programme_id
                     where a.student_id in (select student_id from public.parent_students where parent_id = auth.uid())
                       and p.application_deadline >= current_date) as UpcomingDeadlines,
                  (select count(*)::int from public.notifications
                     where user_id = auth.uid() and is_read = false) as Unread
            """, transaction: tx)));

    public Task<IEnumerable<ChildItem>> ChildrenAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<ChildItem>(new CommandDefinition("""
                select p.id, p.full_name as FullName, sa.aps as Aps,
                       s.school_name as SchoolName, s.grade, ps.relationship as Relationship
                from public.parent_students ps
                join public.profiles p on p.id = ps.student_id
                left join public.students s on s.id = ps.student_id
                left join public.student_aps sa on sa.student_id = ps.student_id
                where ps.parent_id = auth.uid()
                order by p.full_name
            """, transaction: tx)));

    public Task<ChildDetail?> ChildAsync(Guid childId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<ChildDetail>(new CommandDefinition("""
                select p.id, p.full_name as FullName, p.email::text as Email,
                       s.province, s.school_name as SchoolName, s.grade, sa.aps as Aps
                from public.profiles p
                left join public.students s on s.id = p.id
                left join public.student_aps sa on sa.student_id = p.id
                where p.id = @childId
                  and exists (select 1 from public.parent_students where parent_id = auth.uid() and student_id = @childId)
            """, new { childId }, tx)));

    public Task<IEnumerable<ChildApplication>> ApplicationsAsync(Guid childId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<ChildApplication>(new CommandDefinition("""
                select a.id, u.name as University, pr.name as Programme, a.status::text as Status,
                       a.created_at as CreatedAt
                from public.applications a
                join public.universities u on u.id = a.university_id
                join public.programmes pr on pr.id = a.programme_id
                where a.student_id = @childId
                  and exists (select 1 from public.parent_students where parent_id = auth.uid() and student_id = @childId)
                order by a.created_at desc
            """, new { childId }, tx)));

    public Task<IEnumerable<ProgrammeChoice>> ProgrammesAsync(Guid childId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<ProgrammeChoice>(new CommandDefinition("""
                select a.id as ApplicationId, u.name as University, pr.name as Programme, a.status::text as Status
                from public.applications a
                join public.universities u on u.id = a.university_id
                join public.programmes pr on pr.id = a.programme_id
                where a.student_id = @childId
                  and exists (select 1 from public.parent_students where parent_id = auth.uid() and student_id = @childId)
                order by pr.name
            """, new { childId }, tx)));

    public Task<IEnumerable<DeadlineItem>> DeadlinesAsync(Guid childId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<DeadlineItem>(new CommandDefinition("""
                select * from (
                  select 'programme' as Kind, p.name as Title, p.application_deadline as Due
                  from public.applications a
                  join public.programmes p on p.id = a.programme_id
                  where a.student_id = @childId and p.application_deadline >= current_date
                  union all
                  select 'bursary', b.name, b.closes_on
                  from public.bursary_bookmarks bb
                  join public.bursaries b on b.id = bb.bursary_id
                  where bb.student_id = @childId and b.closes_on >= current_date
                ) d
                where exists (select 1 from public.parent_students where parent_id = auth.uid() and student_id = @childId)
                order by Due
            """, new { childId }, tx)));
}

[ApiController]
[Route("api/parent")]
[Authorize(Policy = "Parent")]
public sealed class ParentController(ParentRepo repo) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ParentSummary>> Summary() => Ok(await repo.SummaryAsync());

    [HttpGet("children")]
    public async Task<ActionResult<IEnumerable<ChildItem>>> Children() => Ok(await repo.ChildrenAsync());

    [HttpGet("children/{id}")]
    public async Task<ActionResult<ChildDetail>> Child(Guid id)
    {
        var child = await repo.ChildAsync(id);
        return child is null ? NotFound() : Ok(child);
    }

    [HttpGet("children/{id}/applications")]
    public async Task<ActionResult<IEnumerable<ChildApplication>>> Applications(Guid id)
        => Ok(await repo.ApplicationsAsync(id));

    [HttpGet("children/{id}/programmes")]
    public async Task<ActionResult<IEnumerable<ProgrammeChoice>>> Programmes(Guid id)
        => Ok(await repo.ProgrammesAsync(id));

    [HttpGet("children/{id}/deadlines")]
    public async Task<ActionResult<IEnumerable<DeadlineItem>>> Deadlines(Guid id)
        => Ok(await repo.DeadlinesAsync(id));
}
