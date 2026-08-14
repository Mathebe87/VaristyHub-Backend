using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.ContentAdmin;

// ---- Events ----
public record AdminEvent(Guid Id, string Title, string Type, string? Host, string? Location, bool IsOnline,
    int? Capacity, DateTime StartsAt, DateTime? EndsAt, string? Description);
public record NewEvent(string Title, string Type, string? Host, string? Location, bool? IsOnline,
    int? Capacity, DateTime StartsAt, DateTime? EndsAt, string? Description);
public record UpdateEvent(string? Title, string? Type, string? Host, string? Location, bool? IsOnline,
    int? Capacity, DateTime? StartsAt, DateTime? EndsAt, string? Description);

// ---- Accommodations (amenities array -> init-property) ----
public record AdminAccommodation
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public decimal PricePerMonth { get; init; }
    public string? Campus { get; init; }
    public string? DistanceText { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public decimal? Rating { get; init; }
    public int ReviewsCount { get; init; }
    public string[] Amenities { get; init; } = [];
    public bool IsVerified { get; init; }
    public bool NsfasAccredited { get; init; }
    public bool IsActive { get; init; }
}
public record NewAccommodation(string Name, string Type, decimal PricePerMonth, string? Campus, string? DistanceText,
    decimal? Latitude, decimal? Longitude, string[]? Amenities, bool? NsfasAccredited);
public record UpdateAccommodation(string? Name, string? Type, decimal? PricePerMonth, string? Campus, string? DistanceText,
    decimal? Latitude, decimal? Longitude, string[]? Amenities, bool? IsVerified, bool? NsfasAccredited, bool? IsActive);

// ---- Faculties (super-admin, any university) ----
public record AdminFaculty(Guid Id, Guid UniversityId, string Name);
public record NewAdminFaculty(Guid UniversityId, string Name);
public record RenameAdminFaculty(string Name);

public sealed class ContentAdminRepo(SupabaseDb db)
{
    // Events
    private const string EventCols = """
        select id, title, type::text as Type, host, location, is_online as IsOnline, capacity,
               starts_at as StartsAt, ends_at as EndsAt, description from public.events
        """;

    public Task<IEnumerable<AdminEvent>> ListEventsAsync() =>
        db.AsServiceAsync(async (c, tx) => await c.QueryAsync<AdminEvent>(new CommandDefinition($"{EventCols} order by starts_at desc", transaction: tx)));

    public async Task<AdminEvent> CreateEventAsync(NewEvent n, Guid caller)
    {
        var id = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.events (title, type, host, location, is_online, capacity, starts_at, ends_at, description, created_by)
                values (@Title, @Type::event_type, @Host, @Location, coalesce(@IsOnline, false), @Capacity, @StartsAt, @EndsAt, @Description, @caller)
                returning id
            """, new { n.Title, n.Type, n.Host, n.Location, n.IsOnline, n.Capacity, n.StartsAt, n.EndsAt, n.Description, caller }, tx)));
        return (await GetEventAsync(id))!;
    }

    public Task<AdminEvent?> GetEventAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) => await c.QueryFirstOrDefaultAsync<AdminEvent>(new CommandDefinition($"{EventCols} where id = @id", new { id }, tx)));

    public async Task<AdminEvent?> UpdateEventAsync(Guid id, UpdateEvent u)
    {
        var affected = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.events set title = coalesce(@Title, title), type = coalesce(@Type::event_type, type),
                    host = coalesce(@Host, host), location = coalesce(@Location, location),
                    is_online = coalesce(@IsOnline, is_online), capacity = coalesce(@Capacity, capacity),
                    starts_at = coalesce(@StartsAt, starts_at), ends_at = coalesce(@EndsAt, ends_at),
                    description = coalesce(@Description, description)
                where id = @id
            """, new { id, u.Title, u.Type, u.Host, u.Location, u.IsOnline, u.Capacity, u.StartsAt, u.EndsAt, u.Description }, tx)));
        return affected == 0 ? null : await GetEventAsync(id);
    }

    public Task<bool> DeleteEventAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) => await c.ExecuteAsync(new CommandDefinition("delete from public.events where id = @id", new { id }, tx)) > 0);

    // Accommodations
    private const string AccCols = """
        select id, name, type::text as Type, price_per_month as PricePerMonth, campus, distance_text as DistanceText,
               latitude, longitude, rating, reviews_count as ReviewsCount, amenities as Amenities,
               is_verified as IsVerified, nsfas_accredited as NsfasAccredited, is_active as IsActive
        from public.accommodations
        """;

    public Task<IEnumerable<AdminAccommodation>> ListAccommodationsAsync() =>
        db.AsServiceAsync(async (c, tx) => await c.QueryAsync<AdminAccommodation>(new CommandDefinition($"{AccCols} order by created_at desc", transaction: tx)));

    public async Task<AdminAccommodation> CreateAccommodationAsync(NewAccommodation n)
    {
        var id = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.accommodations (name, type, price_per_month, campus, distance_text, latitude, longitude, amenities, nsfas_accredited, is_active)
                values (@Name, @Type::accommodation_type, @PricePerMonth, @Campus, @DistanceText, @Latitude, @Longitude,
                        coalesce(@Amenities::text[], '{}'::text[]), coalesce(@NsfasAccredited, false), true)
                returning id
            """, new { n.Name, n.Type, n.PricePerMonth, n.Campus, n.DistanceText, n.Latitude, n.Longitude, n.Amenities, n.NsfasAccredited }, tx)));
        return (await GetAccommodationAsync(id))!;
    }

    public Task<AdminAccommodation?> GetAccommodationAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) => await c.QueryFirstOrDefaultAsync<AdminAccommodation>(new CommandDefinition($"{AccCols} where id = @id", new { id }, tx)));

    public async Task<AdminAccommodation?> UpdateAccommodationAsync(Guid id, UpdateAccommodation u)
    {
        var affected = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.accommodations set name = coalesce(@Name, name), type = coalesce(@Type::accommodation_type, type),
                    price_per_month = coalesce(@PricePerMonth, price_per_month), campus = coalesce(@Campus, campus),
                    distance_text = coalesce(@DistanceText, distance_text), latitude = coalesce(@Latitude, latitude),
                    longitude = coalesce(@Longitude, longitude), amenities = coalesce(@Amenities::text[], amenities),
                    is_verified = coalesce(@IsVerified, is_verified), nsfas_accredited = coalesce(@NsfasAccredited, nsfas_accredited),
                    is_active = coalesce(@IsActive, is_active), updated_at = now()
                where id = @id
            """, new { id, u.Name, u.Type, u.PricePerMonth, u.Campus, u.DistanceText, u.Latitude, u.Longitude, u.Amenities, u.IsVerified, u.NsfasAccredited, u.IsActive }, tx)));
        return affected == 0 ? null : await GetAccommodationAsync(id);
    }

    public Task<bool> DeleteAccommodationAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) => await c.ExecuteAsync(new CommandDefinition("delete from public.accommodations where id = @id", new { id }, tx)) > 0);

    // Faculties (any university)
    public Task<IEnumerable<AdminFaculty>> ListFacultiesAsync(Guid? universityId) =>
        db.AsServiceAsync(async (c, tx) => await c.QueryAsync<AdminFaculty>(new CommandDefinition("""
            select id, university_id as UniversityId, name from public.faculties
            where (@universityId is null or university_id = @universityId) order by name
        """, new { universityId }, tx)));

    public Task<Guid> CreateFacultyAsync(NewAdminFaculty n) =>
        db.AsServiceAsync(async (c, tx) => await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into public.faculties (university_id, name) values (@UniversityId, @Name)
            on conflict (university_id, name) do update set name = excluded.name returning id
        """, n, tx)));

    public Task<bool> RenameFacultyAsync(Guid id, string name) =>
        db.AsServiceAsync(async (c, tx) => await c.ExecuteAsync(new CommandDefinition(
            "update public.faculties set name = @name where id = @id", new { id, name }, tx)) > 0);

    public Task<bool> DeleteFacultyAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) => await c.ExecuteAsync(new CommandDefinition(
            "delete from public.faculties where id = @id", new { id }, tx)) > 0);
}

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminContentController(ContentAdminRepo repo, IUserContext me, IAuditService audit) : ControllerBase
{
    private Guid Caller => Guid.Parse(me.UserId!);

    // Events
    [HttpGet("events")]
    public async Task<ActionResult<IEnumerable<AdminEvent>>> Events() => Ok(await repo.ListEventsAsync());

    [HttpPost("events")]
    public async Task<ActionResult<AdminEvent>> CreateEvent([FromBody] NewEvent body)
    {
        var e = await repo.CreateEventAsync(body, Caller);
        await audit.LogAsync(Caller, "event.created", "event", e.Id, new { e.Title });
        return Ok(e);
    }

    [HttpPatch("events/{id}")]
    public async Task<ActionResult<AdminEvent>> UpdateEvent(Guid id, [FromBody] UpdateEvent body)
    {
        var e = await repo.UpdateEventAsync(id, body);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpDelete("events/{id}")]
    public async Task<IActionResult> DeleteEvent(Guid id)
        => await repo.DeleteEventAsync(id) ? NoContent() : NotFound();

    // Accommodations
    [HttpGet("accommodations")]
    public async Task<ActionResult<IEnumerable<AdminAccommodation>>> Accommodations() => Ok(await repo.ListAccommodationsAsync());

    [HttpPost("accommodations")]
    public async Task<ActionResult<AdminAccommodation>> CreateAccommodation([FromBody] NewAccommodation body)
    {
        var a = await repo.CreateAccommodationAsync(body);
        await audit.LogAsync(Caller, "accommodation.created", "accommodation", a.Id, new { a.Name });
        return Ok(a);
    }

    [HttpPatch("accommodations/{id}")]
    public async Task<ActionResult<AdminAccommodation>> UpdateAccommodation(Guid id, [FromBody] UpdateAccommodation body)
    {
        var a = await repo.UpdateAccommodationAsync(id, body);
        return a is null ? NotFound() : Ok(a);
    }

    [HttpDelete("accommodations/{id}")]
    public async Task<IActionResult> DeleteAccommodation(Guid id)
        => await repo.DeleteAccommodationAsync(id) ? NoContent() : NotFound();

    // Faculties
    [HttpGet("faculties")]
    public async Task<ActionResult<IEnumerable<AdminFaculty>>> Faculties([FromQuery] Guid? universityId)
        => Ok(await repo.ListFacultiesAsync(universityId));

    [HttpPost("faculties")]
    public async Task<ActionResult<object>> CreateFaculty([FromBody] NewAdminFaculty body)
    {
        var id = await repo.CreateFacultyAsync(body);
        await audit.LogAsync(Caller, "faculty.created", "faculty", id, new { body.Name });
        return Ok(new { id });
    }

    [HttpPatch("faculties/{id}")]
    public async Task<IActionResult> RenameFaculty(Guid id, [FromBody] RenameAdminFaculty body)
        => await repo.RenameFacultyAsync(id, body.Name) ? NoContent() : NotFound();

    [HttpDelete("faculties/{id}")]
    public async Task<IActionResult> DeleteFaculty(Guid id)
        => await repo.DeleteFacultyAsync(id) ? NoContent() : NotFound();
}
