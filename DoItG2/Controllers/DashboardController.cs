using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DoItG2.Services;

namespace DoItG2.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IDashboardService dashboard, ILogger<DashboardController> logger)
    {
        _dashboard = dashboard;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? "";
        var userType = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "STAFF";
        var stats = await _dashboard.GetStatsAsync(username, userType);
        return View(stats);
    }
}
