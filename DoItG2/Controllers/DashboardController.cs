using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DoItG2.Services;

namespace DoItG2.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ILogger<DashboardController> _logger;

    private readonly IEmailNotificationService _notification;

    public DashboardController(IDashboardService dashboard, IEmailNotificationService notification, ILogger<DashboardController> logger)
    {
        _dashboard = dashboard;
        _notification = notification;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? "";
        var userType = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "STAFF";
        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
        var stats = await _dashboard.GetStatsAsync(username, userType, entity);
        return View(stats);
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var username = User.Identity?.Name ?? "";
        var items = await _notification.GetUserNotificationsAsync(username, 10);
        var unread = await _notification.GetUnreadCountAsync(username);
        return Json(new { success = true, items, unreadCount = unread });
    }

    [HttpPost]
    public async Task<IActionResult> MarkNotifRead(int id)
    {
        var success = await _notification.MarkAsReadAsync(id);
        return Json(new { success });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllNotifsRead()
    {
        var username = User.Identity?.Name ?? "";
        var success = await _notification.MarkAllAsReadAsync(username);
        return Json(new { success });
    }
}
