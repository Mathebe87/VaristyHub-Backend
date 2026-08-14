using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Notifications;

/// <summary>
/// Notifications endpoints (read-only for users).
/// Admin/service can send notifications via NotificationService.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>
    /// Get all notifications for the current user (paginated).
    /// Unread notifications appear first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDetail>>> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = Guid.Parse(User.FindFirst("sub")?.Value ?? "");
        var notifications = await notificationService.GetForUserAsync(userId, page, pageSize);
        return Ok(notifications);
    }

    /// <summary>
    /// Mark a notification as read.
    /// </summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await notificationService.MarkReadAsync(id);
        return NoContent();
    }

    private Guid UserId => Guid.Parse(User.FindFirst("sub")?.Value ?? "");

    /// <summary>Unread count for the current user (bell badge).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount()
        => Ok(new { count = await notificationService.UnreadCountAsync(UserId) });

    /// <summary>Mark all of the current user's notifications read.</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await notificationService.MarkAllReadAsync(UserId);
        return NoContent();
    }
}
