using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Programmes;

public record ProgrammeDto(
    Guid Id, string Name, string Qualification, int MinAps,
    string University, string ShortCode, string? Faculty,
    decimal? TuitionPerYear, decimal? DurationYears, DateTime? ApplicationDeadline);

/// <summary>Public programme catalog with filtering by university, faculty and max APS.</summary>
public sealed class ProgrammeRepo(SupabaseDb db, IUserContext me)
{
    public Task<IEnumerable<ProgrammeDto>> SearchAsync(Guid? universityId, string? faculty, int? minAps) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<ProgrammeDto>(new CommandDefinition("""
                select p.id, p.name, p.qualification::text as Qualification, p.min_aps as MinAps,
                       u.name as University, u.short_code as ShortCode, f.name as Faculty,
                       p.tuition_per_year as TuitionPerYear, p.duration_years as DurationYears,
                       p.application_deadline as ApplicationDeadline
                from public.programmes p
                join public.universities u on u.id = p.university_id
                left join public.faculties f on f.id = p.faculty_id
                where p.is_active
                  and (@universityId is null or p.university_id = @universityId)
                  and (@faculty is null or f.name ilike '%' || @faculty || '%')
                  and (@minAps is null or p.min_aps <= @minAps)
                order by u.name, p.name
            """, new { universityId, faculty, minAps }, tx)));
}

[ApiController]
[Route("api/[controller]")]
public sealed class ProgrammesController(ProgrammeRepo repo) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ProgrammeDto>>> Search(
        [FromQuery] Guid? universityId, [FromQuery] string? faculty, [FromQuery] int? minAps)
        => Ok(await repo.SearchAsync(universityId, faculty, minAps));
}
