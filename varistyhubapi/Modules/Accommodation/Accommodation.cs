using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Accommodation;

// Init-property record: Dapper can't materialize positional records with an array
// constructor param (text[] Amenities), so map by property.
public record AccommodationDto
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
    public bool IsFavourited { get; init; }
}

public sealed class AccommodationRepo(SupabaseDb db, IUserContext me)
{
    public Task<IEnumerable<AccommodationDto>> ListAsync(string? type, string? campus, bool nsfasOnly) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<AccommodationDto>(new CommandDefinition("""
                select a.id, a.name, a.type::text as Type, a.price_per_month as PricePerMonth,
                       a.campus, a.distance_text as DistanceText, a.latitude, a.longitude,
                       a.rating, a.reviews_count as ReviewsCount, a.amenities as Amenities,
                       a.is_verified as IsVerified, a.nsfas_accredited as NsfasAccredited,
                       exists(select 1 from public.accommodation_favourites f
                              where f.accommodation_id = a.id and f.student_id = auth.uid()) as IsFavourited
                from public.accommodations a
                where a.is_active
                  and (@type is null or a.type::text = @type)
                  and (@campus is null or a.campus ilike '%' || @campus || '%')
                  and (@nsfasOnly = false or a.nsfas_accredited = true)
                order by a.price_per_month
            """, new { type, campus, nsfasOnly }, tx)));

    public Task FavouriteAsync(Guid accommodationId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.accommodation_favourites (accommodation_id, student_id)
                values (@accommodationId, auth.uid()) on conflict do nothing
            """, new { accommodationId }, tx));
            return 0;
        });

    public Task RemoveFavouriteAsync(Guid accommodationId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                delete from public.accommodation_favourites
                where accommodation_id = @accommodationId and student_id = auth.uid()
            """, new { accommodationId }, tx));
            return 0;
        });
}

[ApiController]
[Route("api/[controller]")]
public sealed class AccommodationsController(AccommodationRepo repo) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<AccommodationDto>>> List(
        [FromQuery] string? type, [FromQuery] string? campus, [FromQuery] bool nsfasOnly = false)
        => Ok(await repo.ListAsync(type, campus, nsfasOnly));

    [HttpPost("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> Favourite(Guid id)
    {
        await repo.FavouriteAsync(id);
        return NoContent();
    }

    [HttpDelete("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> RemoveFavourite(Guid id)
    {
        await repo.RemoveFavouriteAsync(id);
        return NoContent();
    }
}
