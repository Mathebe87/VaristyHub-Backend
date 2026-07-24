using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Marketplace;

// Init-property record so Dapper can map the text[] Images column (see BursaryDto note).
public record ListingDto
{
    public Guid Id { get; init; }
    public Guid SellerId { get; init; }
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Price { get; init; }
    public string Condition { get; init; } = "";
    public string? Campus { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = "";
    public string[] Images { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record NewListing(string Title, string Category, decimal Price, string Condition, string? Campus, string? Description);
public record MessageDto(Guid Id, Guid SenderId, string Body, DateTime CreatedAt);
public record NewMessage(string Body);
public record RateSeller(int Rating, string? Comment);

public sealed class MarketplaceRepo(SupabaseDb db, IUserContext me)
{
    public Task<IEnumerable<ListingDto>> ListAsync(string? category, string? campus) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<ListingDto>(new CommandDefinition("""
                select l.id, l.seller_id as SellerId, l.title, l.category::text as Category, l.price,
                       l.condition::text as Condition, l.campus, l.description, l.status::text as Status,
                       coalesce((select array_agg(li.storage_path order by li.position)
                                 from public.listing_images li where li.listing_id = l.id), '{}') as Images,
                       l.created_at as CreatedAt
                from public.marketplace_listings l
                where l.status = 'active'
                  and (@category is null or l.category::text = @category)
                  and (@campus is null or l.campus ilike '%' || @campus || '%')
                order by l.created_at desc
            """, new { category, campus }, tx)));

    public Task<Guid> CreateAsync(NewListing n) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.marketplace_listings
                    (seller_id, title, category, price, condition, campus, description, status)
                values (auth.uid(), @Title, @Category::listing_category, @Price,
                        @Condition::listing_condition, @Campus, @Description, 'active')
                returning id
            """, n, tx)));

    public Task WishlistAsync(Guid listingId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.listing_wishlist (listing_id, student_id)
                values (@listingId, auth.uid()) on conflict do nothing
            """, new { listingId }, tx));
            return 0;
        });

    public Task RemoveWishlistAsync(Guid listingId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.listing_wishlist where listing_id = @listingId and student_id = auth.uid()",
                new { listingId }, tx));
            return 0;
        });

    public Task<IEnumerable<MessageDto>> GetMessagesAsync(Guid listingId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<MessageDto>(new CommandDefinition("""
                select m.id, m.sender_id as SenderId, m.body, m.created_at as CreatedAt
                from public.marketplace_messages m
                join public.marketplace_conversations conv on conv.id = m.conversation_id
                where conv.listing_id = @listingId
                  and (conv.buyer_id = auth.uid() or conv.seller_id = auth.uid())
                order by m.created_at
            """, new { listingId }, tx)));

    public Task<Guid> SendMessageAsync(Guid listingId, string body) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            // Find an existing conversation I'm part of for this listing.
            var convId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                select id from public.marketplace_conversations
                where listing_id = @listingId and (buyer_id = auth.uid() or seller_id = auth.uid())
                limit 1
            """, new { listingId }, tx));

            if (convId is null)
            {
                // New conversation: current user is the buyer, listing owner is the seller.
                convId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                    insert into public.marketplace_conversations (listing_id, buyer_id, seller_id)
                    select @listingId, auth.uid(), l.seller_id
                    from public.marketplace_listings l where l.id = @listingId
                    returning id
                """, new { listingId }, tx));
            }

            return await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.marketplace_messages (conversation_id, sender_id, body)
                values (@convId, auth.uid(), @body)
                returning id
            """, new { convId, body }, tx));
        });

    public Task RateAsync(Guid listingId, int rating, string? comment) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.seller_ratings (listing_id, seller_id, rater_id, rating, comment)
                select @listingId, l.seller_id, auth.uid(), @rating, @comment
                from public.marketplace_listings l where l.id = @listingId
            """, new { listingId, rating, comment }, tx));
            return 0;
        });
}

[ApiController]
[Route("api/listings")]
public sealed class ListingsController(MarketplaceRepo repo) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ListingDto>>> List(
        [FromQuery] string? category, [FromQuery] string? campus)
        => Ok(await repo.ListAsync(category, campus));

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<object>> Create([FromBody] NewListing body)
    {
        var id = await repo.CreateAsync(body);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpPost("{id}/wishlist")]
    [Authorize]
    public async Task<IActionResult> Wishlist(Guid id)
    {
        await repo.WishlistAsync(id);
        return NoContent();
    }

    [HttpDelete("{id}/wishlist")]
    [Authorize]
    public async Task<IActionResult> RemoveWishlist(Guid id)
    {
        await repo.RemoveWishlistAsync(id);
        return NoContent();
    }

    [HttpGet("{id}/messages")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetMessages(Guid id)
        => Ok(await repo.GetMessagesAsync(id));

    [HttpPost("{id}/messages")]
    [Authorize]
    public async Task<ActionResult<object>> SendMessage(Guid id, [FromBody] NewMessage body)
        => Ok(new { id = await repo.SendMessageAsync(id, body.Body) });

    [HttpPost("{id}/rate")]
    [Authorize]
    public async Task<IActionResult> Rate(Guid id, [FromBody] RateSeller body)
    {
        await repo.RateAsync(id, body.Rating, body.Comment);
        return NoContent();
    }
}
