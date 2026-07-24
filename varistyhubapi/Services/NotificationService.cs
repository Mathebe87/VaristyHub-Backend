using System.Data;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Implementation of notification service.
/// Inserts notifications via service role (RLS bypassed).
/// Frontend subscribes to real-time updates via Supabase Realtime.
/// </summary>
public sealed class NotificationService(SupabaseDb db) : INotificationService
{
    /// <summary>
    /// Send a notification to a user.
    /// Inserts a row in public.notifications (RLS policy: only super_admin can insert).
    /// </summary>
    public Task NotifyAsync(Guid userId, string category, string title, string? body, string? url) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.notifications (user_id, category, title, body, action_url)
                values (@userId, @category::notification_category, @title, @body, @url)
            """, new { userId, category, title, body, url }, tx));
            return 0;
        });

    /// <summary>
    /// Mark a notification as read.
    /// Via service role (RLS bypassed) since notifications use an RLS policy.
    /// </summary>
    public Task MarkReadAsync(Guid notificationId) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update public.notifications
                set is_read = true
                where id = @notificationId
            """, new { notificationId }, tx));
            return 0;
        });

    /// <summary>
    /// Get all notifications for a user.
    /// Unread notifications appear first, then paginated.
    /// </summary>
    public Task<IEnumerable<NotificationDetail>> GetForUserAsync(Guid userId, int page = 1, int pageSize = 20) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var offset = (page - 1) * pageSize;
            return await c.QueryAsync<NotificationDetail>(new CommandDefinition("""
                select id, category, title, body, action_url as ActionUrl,
                       is_read as IsRead, created_at as CreatedAt
                from public.notifications
                where user_id = @userId
                order by is_read asc, created_at desc
                limit @pageSize offset @offset
            """, new { userId, pageSize, offset }, tx));
        });
}
