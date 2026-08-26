using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DoItG2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoItG2.Controllers;

[Authorize]
[ApiController]
public class DocumentLockController : ControllerBase
{
    private readonly IDocumentLockService _lockService;

    public DocumentLockController(IDocumentLockService lockService)
    {
        _lockService = lockService;
    }

    public class LockRequestModel
    {
        public string Car { get; set; } = string.Empty;
        public string DocType { get; set; } = "PIB"; // PIB or PEB
    }

    [HttpPost("api/doc-lock/acquire")]
    public async Task<IActionResult> AcquireLock([FromBody] LockRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Car))
            return BadRequest(new { message = "Nomor CAR tidak boleh kosong." });

        var username = User.Identity?.Name ?? "unknown";
        var fullName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? username;
        var entity = User.FindFirst("Entity")?.Value ?? "SIM";

        var result = await _lockService.AcquireLockAsync(model.Car, model.DocType, username, fullName, entity);
        return Ok(new
        {
            isLocked = result.isLocked,
            lockedByName = result.lockedByName,
            lockedByUser = result.lockedByUser,
            lockedAt = result.lockedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            expiresAt = result.expiresAt?.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    [HttpPost("api/doc-lock/heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] LockRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Car)) return BadRequest();
        var username = User.Identity?.Name ?? "unknown";
        var success = await _lockService.HeartbeatLockAsync(model.Car, username);
        return Ok(new { success });
    }

    [HttpPost("api/doc-lock/release")]
    public async Task<IActionResult> Release([FromBody] LockRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Car)) return BadRequest();
        var username = User.Identity?.Name ?? "unknown";
        var success = await _lockService.ReleaseLockAsync(model.Car, username);
        return Ok(new { success });
    }

    [HttpPost("api/doc-lock/force-unlock")]
    public async Task<IActionResult> ForceUnlock([FromBody] LockRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Car)) return BadRequest();

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isAdmin = role.Contains("ADMIN", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            return StatusCode(403, new { message = "Hanya Admin Dokumen yang berwenang membuka paksa kunci dokumen (Force Unlock)." });
        }

        var username = User.Identity?.Name ?? "unknown";
        var success = await _lockService.ForceUnlockAsync(model.Car, username);
        return Ok(new { success, message = $"Kunci dokumen {model.Car} berhasil dibuka oleh Administrator." });
    }

    [HttpGet("api/doc-lock/check-car-duplicate")]
    public async Task<IActionResult> CheckCarDuplicate([FromQuery] string car, [FromQuery] string docType = "")
    {
        if (string.IsNullOrWhiteSpace(car))
            return Ok(new { isDuplicate = false, message = "" });

        var result = await _lockService.CheckCarDuplicateAsync(car, docType);
        return Ok(new
        {
            isDuplicate = result.isDuplicate,
            message = result.message
        });
    }

    [HttpGet("api/doc-lock/active")]
    public async Task<IActionResult> GetActiveLocks()
    {
        var locks = await _lockService.GetActiveLocksAsync();
        return Ok(locks);
    }
}
