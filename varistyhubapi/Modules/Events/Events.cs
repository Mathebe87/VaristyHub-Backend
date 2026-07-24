using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Events;

public record EventDto(
    Guid Id, string Title, string Type, string? Host, string? Location, bool IsOnline,
    int? Capacity, DateTime StartsAt, DateTime? EndsAt, string? Description, bool IsRegistered);

public sealed class EventRepo(SupabaseDb db, IUserContext me)
{
    public Task<IEnumerable<EventDto>> ListAsync(string? type, bool upcomingOnly) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<EventDto>(new CommandDefinition("""
                select e.id, e.title, e.type::text as Type, e.host, e.location, e.is_online as IsOnline,
                       e.capacity, e.starts_at as StartsAt, e.ends_at as EndsAt, e.description,
                       exists(select 1 from public.event_registrations r
                              where r.event_id = e.id and r.student_id = auth.uid()) as IsRegistered
                from public.events e
                where (@type is null or e.type::text = @type)
                  and (@upcomingOnly = false or e.starts_at >= now())
                order by e.starts_at
            """, new { type, upcomingOnly }, tx)));

    public Task RegisterAsync(Guid eventId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.event_registrations (event_id, student_id)
                values (@eventId, auth.uid()) on conflict do nothing
            """, new { eventId }, tx));
            return 0;
        });

    public Task UnregisterAsync(Guid eventId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.event_registrations where event_id = @eventId and student_id = auth.uid()",
                new { eventId }, tx));
            return 0;
        });
}

[ApiController]
[Route("api/[controller]")]
public sealed class EventsController(EventRepo repo) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EventDto>>> List(
        [FromQuery] string? type, [FromQuery] bool upcomingOnly = true)
        => Ok(await repo.ListAsync(type, upcomingOnly));

    [HttpPost("{id}/register")]
    [Authorize]
    public async Task<IActionResult> Register(Guid id)
    {
        await repo.RegisterAsync(id);
        return NoContent();
    }

    [HttpDelete("{id}/register")]
    [Authorize]
    public async Task<IActionResult> Unregister(Guid id)
    {
        await repo.UnregisterAsync(id);
        return NoContent();
    }
}
