using System.Data;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Bursaries;

// Init-property record: Dapper can't materialize a positional record whose constructor
// has an array parameter (text[] Covers), so map by property instead.
public record BursaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Field { get; init; } = "";
    public string? AmountText { get; init; }
    public string[] Covers { get; init; } = [];
    public int? MinAps { get; init; }
    public string? Description { get; init; }
    public DateTime? ClosesOn { get; init; }
}

public sealed class BursaryRepo(SupabaseDb db, IUserContext me, INotificationService notify)
{
    public Task<IEnumerable<BursaryDto>> ListAsync(string? field, int? maxAps) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<BursaryDto>(new CommandDefinition("""
                select id, name, provider, field::text as Field, amount_text as AmountText,
                       covers as Covers, min_aps as MinAps, description, closes_on as ClosesOn
                from public.bursaries
                where is_active
                  and (@field is null or field::text = @field)
                  and (@maxAps is null or min_aps is null or min_aps <= @maxAps)
                order by coalesce(closes_on, 'infinity'::date), name
            """, new { field, maxAps }, tx)));

    public async Task<Guid> ApplyAsync(Guid bursaryId)
    {
        Guid appId; Guid? createdBy; string? name;
        try
        {
            (appId, createdBy, name) = await db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            {
                var id = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                    insert into public.bursary_applications (bursary_id, student_id, status)
                    values (@bursaryId, auth.uid(), 'submitted')
                    returning id
                """, new { bursaryId }, tx));
                var info = await c.QueryFirstOrDefaultAsync<(Guid? CreatedBy, string? Name)>(new CommandDefinition(
                    "select created_by, name from public.bursaries where id = @bursaryId", new { bursaryId }, tx));
                return (id, info.CreatedBy, info.Name);
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException("You have already applied to this bursary.");
        }

        if (createdBy is Guid cb)
            await notify.NotifyAsync(cb, "bursary", "New bursary application",
                $"A student applied to '{name}'.", "/bursaries");
        return appId;
    }

    public Task BookmarkAsync(Guid bursaryId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.bursary_bookmarks (bursary_id, student_id)
                values (@bursaryId, auth.uid()) on conflict do nothing
            """, new { bursaryId }, tx));
            return 0;
        });

    public Task RemoveBookmarkAsync(Guid bursaryId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                delete from public.bursary_bookmarks
                where bursary_id = @bursaryId and student_id = auth.uid()
            """, new { bursaryId }, tx));
            return 0;
        });
}

[ApiController]
[Route("api/[controller]")]
public sealed class BursariesController(BursaryRepo repo, RecommendationService recs) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<BursaryDto>>> List(
        [FromQuery] string? field, [FromQuery] int? maxAps)
        => Ok(await repo.ListAsync(field, maxAps));

    [HttpGet("recommended")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<RankedItem>>> Recommended()
        => Ok(await recs.RecommendBursariesAsync());

    [HttpPost("{id}/apply")]
    [Authorize]
    public async Task<ActionResult<object>> Apply(Guid id)
        => Ok(new { id = await repo.ApplyAsync(id) });

    [HttpPost("{id}/bookmark")]
    [Authorize]
    public async Task<IActionResult> Bookmark(Guid id)
    {
        await repo.BookmarkAsync(id);
        return NoContent();
    }

    [HttpDelete("{id}/bookmark")]
    [Authorize]
    public async Task<IActionResult> RemoveBookmark(Guid id)
    {
        await repo.RemoveBookmarkAsync(id);
        return NoContent();
    }
}
