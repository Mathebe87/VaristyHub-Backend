using System.Data;
using System.Text.Json;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Writes to public.audit_logs for sensitive actions (application status changes,
/// payments, admin edits, role changes). Runs on the service path so RLS can't block it.
/// </summary>
public interface IAuditService
{
    Task LogAsync(Guid? actorId, string action, string? entityType, Guid? entityId, object? metadata = null);
}

public sealed class AuditService(SupabaseDb db) : IAuditService
{
    public Task LogAsync(Guid? actorId, string action, string? entityType, Guid? entityId, object? metadata = null) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.audit_logs (actor_id, action, entity_type, entity_id, metadata)
                values (@actorId, @action, @entityType, @entityId, @meta::jsonb)
            """, new
            {
                actorId,
                action,
                entityType,
                entityId,
                meta = JsonSerializer.Serialize(metadata ?? new { })
            }, tx));
            return 0;
        });
}
