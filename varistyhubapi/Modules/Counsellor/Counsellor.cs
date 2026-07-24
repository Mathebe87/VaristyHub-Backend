using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Counsellor;

public record CounsellorSummary(int Learners, int? AvgAps, int InProgress, int MissingDocs);
public record LearnerListItem(Guid Id, string FullName, string? Email, int? Aps, string? SchoolName, string? Grade);
public record LearnerDetail(Guid Id, string FullName, string? Email, string? Phone, string? Province, string? SchoolName, string? Grade, int? Aps);
public record ResultRow(Guid Id, string SubjectName, int Level, int Percentage, bool IsLifeOrientation);
public record CaseApplication(Guid Id, string StudentName, string University, string Programme, string Status, DateTime CreatedAt);
public record MissingDocItem(Guid StudentId, string FullName, Guid UniversityId, string Status);
public record RecommendationDto(Guid Id, string Title, string? Body, DateTime CreatedAt);
public record NewRecommendation(string Title, string? Body);
public record EligibleProgrammeDto(Guid Id, string Name, int MinAps, string University, string ShortCode);
public record StatusCount(string Status, int Count);

/// <summary>
/// Counsellor caseload. Every query runs as the caller (RLS enforced) and is additionally
/// scoped to their linked learners via public.counsellor_students.
/// </summary>
public sealed class CounsellorRepo(SupabaseDb db, IUserContext me)
{
    public Task<CounsellorSummary> SummaryAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstAsync<CounsellorSummary>(new CommandDefinition("""
                select
                  (select count(*)::int from public.counsellor_students where counsellor_id = auth.uid()) as Learners,
                  (select round(avg(sa.aps))::int from public.counsellor_students cs
                     join public.student_aps sa on sa.student_id = cs.student_id
                     where cs.counsellor_id = auth.uid()) as AvgAps,
                  (select count(*)::int from public.applications a
                     where a.student_id in (select student_id from public.counsellor_students where counsellor_id = auth.uid())
                       and a.status not in ('approved','rejected')) as InProgress,
                  (select count(*)::int from public.applications a
                     where a.student_id in (select student_id from public.counsellor_students where counsellor_id = auth.uid())
                       and a.status = 'pending_documents') as MissingDocs
            """, transaction: tx)));

    public Task<IEnumerable<LearnerListItem>> LearnersAsync(string? q) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<LearnerListItem>(new CommandDefinition("""
                select p.id, p.full_name as FullName, p.email::text as Email,
                       sa.aps as Aps, s.school_name as SchoolName, s.grade
                from public.counsellor_students cs
                join public.profiles p on p.id = cs.student_id
                left join public.students s on s.id = cs.student_id
                left join public.student_aps sa on sa.student_id = cs.student_id
                where cs.counsellor_id = auth.uid()
                  and (@q is null or p.full_name ilike '%' || @q || '%')
                order by p.full_name
            """, new { q }, tx)));

    public Task<LearnerDetail?> LearnerAsync(Guid id) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<LearnerDetail>(new CommandDefinition("""
                select p.id, p.full_name as FullName, p.email::text as Email, p.phone,
                       s.province, s.school_name as SchoolName, s.grade, sa.aps as Aps
                from public.profiles p
                left join public.students s on s.id = p.id
                left join public.student_aps sa on sa.student_id = p.id
                where p.id = @id
                  and exists (select 1 from public.counsellor_students
                              where counsellor_id = auth.uid() and student_id = @id)
            """, new { id }, tx)));

    public Task<IEnumerable<ResultRow>> ResultsAsync(Guid id) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<ResultRow>(new CommandDefinition("""
                select r.id, r.subject_name as SubjectName, r.level, r.percentage,
                       r.is_life_orientation as IsLifeOrientation
                from public.student_results r
                where r.student_id = @id
                  and exists (select 1 from public.counsellor_students
                              where counsellor_id = auth.uid() and student_id = @id)
                order by r.subject_name
            """, new { id }, tx)));

    public Task<IEnumerable<CaseApplication>> ApplicationsAsync(string? status) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<CaseApplication>(new CommandDefinition("""
                select a.id, p.full_name as StudentName, u.name as University, pr.name as Programme,
                       a.status::text as Status, a.created_at as CreatedAt
                from public.applications a
                join public.profiles p on p.id = a.student_id
                join public.universities u on u.id = a.university_id
                join public.programmes pr on pr.id = a.programme_id
                where a.student_id in (select student_id from public.counsellor_students where counsellor_id = auth.uid())
                  and (@status is null or a.status::text = @status)
                order by a.created_at desc
            """, new { status }, tx)));

    public Task<IEnumerable<MissingDocItem>> MissingDocsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<MissingDocItem>(new CommandDefinition("""
                select st.id as StudentId, p.full_name as FullName, a.university_id as UniversityId,
                       a.status::text as Status
                from public.counsellor_students cs
                join public.students st on st.id = cs.student_id
                join public.profiles p on p.id = st.id
                join public.applications a on a.student_id = st.id and a.status = 'pending_documents'
                where cs.counsellor_id = auth.uid()
            """, transaction: tx)));

    public Task<IEnumerable<RecommendationDto>> RecommendationsAsync(Guid studentId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<RecommendationDto>(new CommandDefinition("""
                select id, title, body, created_at as CreatedAt
                from public.career_recommendations
                where student_id = @studentId and counsellor_id = auth.uid()
                order by created_at desc
            """, new { studentId }, tx)));

    public Task<Guid> AddRecommendationAsync(Guid studentId, string title, string? body) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.career_recommendations (student_id, counsellor_id, title, body)
                values (@studentId, auth.uid(), @title, @body)
                returning id
            """, new { studentId, title, body }, tx)));

    public Task<IEnumerable<EligibleProgrammeDto>> EligibleProgrammesAsync(Guid studentId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<EligibleProgrammeDto>(new CommandDefinition("""
                with my as (
                  select coalesce((select aps from public.student_aps where student_id = @studentId), 0) as aps
                )
                select p.id, p.name, p.min_aps as MinAps, u.name as University, u.short_code as ShortCode
                from public.programmes p
                join public.universities u on u.id = p.university_id
                cross join my
                where p.is_active and my.aps >= p.min_aps
                  and exists (select 1 from public.counsellor_students where counsellor_id = auth.uid() and student_id = @studentId)
                  and not exists (
                    select 1 from public.programme_requirements r
                    where r.programme_id = p.id
                      and not exists (
                        select 1 from public.student_results sr
                        where sr.student_id = @studentId and sr.subject_name = r.subject_name and sr.level >= r.min_level))
                order by p.min_aps desc
            """, new { studentId }, tx)));

    public Task<IEnumerable<StatusCount>> ReportsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<StatusCount>(new CommandDefinition("""
                select a.status::text as Status, count(*)::int as Count
                from public.applications a
                where a.student_id in (select student_id from public.counsellor_students where counsellor_id = auth.uid())
                group by a.status
                order by count(*) desc
            """, transaction: tx)));
}

[ApiController]
[Route("api/counsellor")]
[Authorize(Policy = "Counsellor")]
public sealed class CounsellorController(CounsellorRepo repo, IAuditService audit, IUserContext me) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<CounsellorSummary>> Summary() => Ok(await repo.SummaryAsync());

    [HttpGet("learners")]
    public async Task<ActionResult<IEnumerable<LearnerListItem>>> Learners([FromQuery] string? q)
        => Ok(await repo.LearnersAsync(q));

    [HttpGet("learners/{id}")]
    public async Task<ActionResult<LearnerDetail>> Learner(Guid id)
    {
        var l = await repo.LearnerAsync(id);
        return l is null ? NotFound() : Ok(l);
    }

    [HttpGet("learners/{id}/results")]
    public async Task<ActionResult<IEnumerable<ResultRow>>> Results(Guid id)
        => Ok(await repo.ResultsAsync(id));

    [HttpGet("applications")]
    public async Task<ActionResult<IEnumerable<CaseApplication>>> Applications([FromQuery] string? status)
        => Ok(await repo.ApplicationsAsync(status));

    [HttpGet("missing-docs")]
    public async Task<ActionResult<IEnumerable<MissingDocItem>>> MissingDocs()
        => Ok(await repo.MissingDocsAsync());

    [HttpGet("learners/{id}/recommendations")]
    public async Task<ActionResult<IEnumerable<RecommendationDto>>> GetRecommendations(Guid id)
        => Ok(await repo.RecommendationsAsync(id));

    [HttpPost("learners/{id}/recommendations")]
    public async Task<ActionResult<object>> AddRecommendation(Guid id, [FromBody] NewRecommendation body)
    {
        var recId = await repo.AddRecommendationAsync(id, body.Title, body.Body);
        await audit.LogAsync(Guid.Parse(me.UserId!), "recommendation.created", "student", id, new { body.Title });
        return Ok(new { id = recId });
    }

    [HttpGet("learners/{id}/eligible-programmes")]
    public async Task<ActionResult<IEnumerable<EligibleProgrammeDto>>> Eligible(Guid id)
        => Ok(await repo.EligibleProgrammesAsync(id));

    [HttpGet("reports")]
    public async Task<ActionResult<IEnumerable<StatusCount>>> Reports() => Ok(await repo.ReportsAsync());
}
