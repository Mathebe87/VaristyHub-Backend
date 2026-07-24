namespace VarsityHub.Services;

/// <summary>
/// Notification service interface.
/// Inserts notifications into the database (with RLS bypassed via service role).
/// Frontend subscribes to real-time updates via Supabase Realtime.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a notification to a user.
    /// Category: application_update, deadline_reminder, new_message, etc.
    /// </summary>
    Task NotifyAsync(Guid userId, string category, string title, string? body, string? url);

    /// <summary>
    /// Mark a notification as read.
    /// </summary>
    Task MarkReadAsync(Guid notificationId);

    /// <summary>
    /// Get all notifications for a user (paginated, unread first).
    /// </summary>
    Task<IEnumerable<NotificationDetail>> GetForUserAsync(Guid userId, int page = 1, int pageSize = 20);
}

/// <summary>
/// A notification detail returned to the client.
/// </summary>
public record NotificationDetail(Guid Id, string Category, string Title, string? Body, string? ActionUrl, bool IsRead, DateTime CreatedAt);
