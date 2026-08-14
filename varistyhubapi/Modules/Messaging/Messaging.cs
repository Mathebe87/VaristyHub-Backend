using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Messaging;

public record ConversationDto(Guid Id, string? Subject, DateTime CreatedAt,
    string? LastMessage, DateTime? LastMessageAt, int UnreadCount);
public record MessageDto(Guid Id, Guid SenderId, string Body, DateTime? ReadAt, DateTime CreatedAt);
public record NewConversation(string? Subject, Guid[] ParticipantIds, string? Message);
public record SendMessage(string Body);

/// <summary>
/// Direct messaging (student ↔ counsellor / university). Runs on the service path with explicit
/// participant scoping (the schema's conversation/participant RLS policies recurse into each
/// other — 42P17 — so we can't rely on them here). Access is enforced by @userId in every query.
/// </summary>
public sealed class MessagingRepo(SupabaseDb db, IUserContext me)
{
    private Guid Me => Guid.Parse(me.UserId!);

    public Task<IEnumerable<ConversationDto>> ListAsync()
    {
        var userId = Me;
        return db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<ConversationDto>(new CommandDefinition("""
                select c.id, c.subject, c.created_at as CreatedAt,
                       (select m.body from public.messages m where m.conversation_id = c.id order by m.created_at desc limit 1) as LastMessage,
                       (select m.created_at from public.messages m where m.conversation_id = c.id order by m.created_at desc limit 1) as LastMessageAt,
                       (select count(*)::int from public.messages m
                          where m.conversation_id = c.id and m.sender_id <> @userId and m.read_at is null) as UnreadCount
                from public.conversations c
                where exists (select 1 from public.conversation_participants p
                              where p.conversation_id = c.id and p.profile_id = @userId)
                order by coalesce((select max(created_at) from public.messages m where m.conversation_id = c.id), c.created_at) desc
            """, new { userId }, tx)));
    }

    public Task<Guid> CreateAsync(NewConversation n)
    {
        var userId = Me;
        return db.AsServiceAsync(async (c, tx) =>
        {
            var convId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "insert into public.conversations (subject, created_by) values (@Subject, @userId) returning id",
                new { n.Subject, userId }, tx));

            var ids = n.ParticipantIds.Append(userId).Distinct();
            foreach (var pid in ids)
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into public.conversation_participants (conversation_id, profile_id) values (@convId, @pid)
                    on conflict do nothing
                """, new { convId, pid }, tx));

            if (!string.IsNullOrWhiteSpace(n.Message))
                await c.ExecuteAsync(new CommandDefinition(
                    "insert into public.messages (conversation_id, sender_id, body) values (@convId, @userId, @Message)",
                    new { convId, userId, n.Message }, tx));

            return convId;
        });
    }

    public Task<IEnumerable<MessageDto>> GetMessagesAsync(Guid convId)
    {
        var userId = Me;
        return db.AsServiceAsync(async (c, tx) =>
        {
            var member = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from public.conversation_participants where conversation_id = @convId and profile_id = @userId)",
                new { convId, userId }, tx));
            if (!member) return [];
            return await c.QueryAsync<MessageDto>(new CommandDefinition("""
                select id, sender_id as SenderId, body, read_at as ReadAt, created_at as CreatedAt
                from public.messages where conversation_id = @convId order by created_at
            """, new { convId }, tx));
        });
    }

    public Task<Guid?> SendMessageAsync(Guid convId, string body)
    {
        var userId = Me;
        return db.AsServiceAsync(async (c, tx) =>
        {
            var member = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from public.conversation_participants where conversation_id = @convId and profile_id = @userId)",
                new { convId, userId }, tx));
            if (!member) return (Guid?)null;
            return await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "insert into public.messages (conversation_id, sender_id, body) values (@convId, @userId, @body) returning id",
                new { convId, userId, body }, tx));
        });
    }

    public Task MarkReadAsync(Guid convId)
    {
        var userId = Me;
        return db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update public.messages set read_at = now()
                where conversation_id = @convId and sender_id <> @userId and read_at is null
                  and exists (select 1 from public.conversation_participants p
                              where p.conversation_id = @convId and p.profile_id = @userId)
            """, new { convId, userId }, tx));
            return 0;
        });
    }
}

[ApiController]
[Route("api/conversations")]
[Authorize]
public sealed class ConversationsController(MessagingRepo repo) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationDto>>> List() => Ok(await repo.ListAsync());

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] NewConversation body)
        => Ok(new { id = await repo.CreateAsync(body) });

    [HttpGet("{id}/messages")]
    public async Task<ActionResult<IEnumerable<MessageDto>>> Messages(Guid id)
        => Ok(await repo.GetMessagesAsync(id));

    [HttpPost("{id}/messages")]
    public async Task<ActionResult<object>> Send(Guid id, [FromBody] SendMessage body)
    {
        var msgId = await repo.SendMessageAsync(id, body.Body);
        return msgId is null ? NotFound(new { error = "Conversation not found." }) : Ok(new { id = msgId });
    }

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await repo.MarkReadAsync(id);
        return NoContent();
    }
}
