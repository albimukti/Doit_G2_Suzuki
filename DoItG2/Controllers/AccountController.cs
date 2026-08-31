using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using DoItG2.Models.Auth;
using DoItG2.Services;

namespace DoItG2.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly IAuditService _audit;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthService auth, IAuditService audit, ILogger<AccountController> logger)
    {
        _auth = auth;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.UserName) || string.IsNullOrWhiteSpace(model.Password))
        {
            if (string.IsNullOrWhiteSpace(model.UserName))
                ModelState.AddModelError(nameof(model.UserName), "Silakan masukkan username Anda.");
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError(nameof(model.Password), "Silakan masukkan password Anda.");
            return View(model);
        }

        var user = await _auth.ValidateUserAsync(model.UserName, model.Password);
        if (user == null)
        {
            var existingUser = await _auth.GetUserByUsernameAsync(model.UserName);
            if (existingUser != null && !existingUser.IsActive)
            {
                ModelState.AddModelError("", "Akun Anda berstatus non-aktif. Silakan hubungi Administrator Sistem.");
                await _audit.LogAsync(model.UserName, "LOGIN_INACTIVE", "AUTH",
                    description: "Login gagal: Akun berstatus non-aktif", ipAddress: GetClientIp(), isError: true);
                return View(model);
            }

            ModelState.AddModelError("", "Kombinasi Username atau Password yang Anda masukkan tidak sesuai. Silakan periksa kembali.");
            await _audit.LogAsync(model.UserName, "LOGIN_FAILED", "AUTH",
                description: "Login gagal: Kredensial tidak valid", ipAddress: GetClientIp(), isError: true);
            return View(model);
        }

        var entity = string.Equals(model.Entity, "SIS", StringComparison.OrdinalIgnoreCase) ? "SIS" : "SIM";

        // Entity Access Validation:
        // SIM user cannot access SIS, SIS user cannot access SIM. Dual access (ALL) can access both.
        if (entity == "SIS" && !user.CanAccessSis)
        {
            ModelState.AddModelError("", $"Akses Ditolak: Akun '{user.UserName}' hanya memiliki izin akses untuk entitas SIM (PT. Suzuki Indomobil Motor) dan tidak dapat masuk ke entitas SIS.");
            await _audit.LogAsync(model.UserName, "LOGIN_DENIED", "AUTH",
                description: $"Akses ditolak: User terdaftar pada entitas SIM mencoba masuk ke SIS", ipAddress: GetClientIp(), isError: true);
            return View(model);
        }

        if (entity == "SIM" && !user.CanAccessSim)
        {
            ModelState.AddModelError("", $"Akses Ditolak: Akun '{user.UserName}' hanya memiliki izin akses untuk entitas SIS (PT. Suzuki Indomobil Sales) dan tidak dapat masuk ke entitas SIM.");
            await _audit.LogAsync(model.UserName, "LOGIN_DENIED", "AUTH",
                description: $"Akses ditolak: User terdaftar pada entitas SIS mencoba masuk ke SIM", ipAddress: GetClientIp(), isError: true);
            return View(model);
        }

        var entityName = entity == "SIS" ? "PT. Suzuki Indomobil Sales" : "PT. Suzuki Indomobil Motor";
        var entityKey = entity == "SIS" ? "84" : "81";
        var entityNpwp = entity == "SIS" ? "01.129.738.9-411.000" : "01.129.737.1-411.000";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.UserType),
            new("Entity", entity),
            new("EntityName", entityName),
            new("EntityKey", entityKey),
            new("EntityNpwp", entityNpwp),
            new("EntityAccess", user.EntityAccess ?? "ALL"),
            new("CanAccessSim", user.CanAccessSim.ToString()),
            new("CanAccessSis", user.CanAccessSis.ToString()),
            new("IsAdmin", user.IsAdmin.ToString()),
            new("PibSim", user.PibSim.ToString()),
            new("PibSis", user.PibSis.ToString()),
            new("PebSim", user.PebSim.ToString()),
            new("PebSis", user.PebSis.ToString()),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe
                ? DateTimeOffset.UtcNow.AddDays(7)
                : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), authProps);

        await _audit.LogAsync(user.UserName, "LOGIN", "AUTH",
            description: $"Login berhasil sebagai {entity} ({entityName})", ipAddress: GetClientIp());

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchEntity(string targetEntity, string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login");

        var username = User.Identity.Name ?? "";
        var user = await _auth.GetUserByUsernameAsync(username);
        if (user == null || !user.IsActive)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        var requestedEntity = string.Equals(targetEntity, "SIS", StringComparison.OrdinalIgnoreCase) ? "SIS" : "SIM";

        if (requestedEntity == "SIS" && !user.CanAccessSis)
        {
            TempData["Error"] = "Akses ditolak: Akun Anda tidak memiliki izin untuk mengakses entitas SIS (PT. Suzuki Indomobil Sales).";
            return RedirectToAction("Index", "Dashboard");
        }

        if (requestedEntity == "SIM" && !user.CanAccessSim)
        {
            TempData["Error"] = "Akses ditolak: Akun Anda tidak memiliki izin untuk mengakses entitas SIM (PT. Suzuki Indomobil Motor).";
            return RedirectToAction("Index", "Dashboard");
        }

        var entityName = requestedEntity == "SIS" ? "PT. Suzuki Indomobil Sales" : "PT. Suzuki Indomobil Motor";
        var entityKey = requestedEntity == "SIS" ? "84" : "81";
        var entityNpwp = requestedEntity == "SIS" ? "01.129.738.9-411.000" : "01.129.737.1-411.000";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.UserType),
            new("Entity", requestedEntity),
            new("EntityName", entityName),
            new("EntityKey", entityKey),
            new("EntityNpwp", entityNpwp),
            new("EntityAccess", user.EntityAccess ?? "ALL"),
            new("CanAccessSim", user.CanAccessSim.ToString()),
            new("CanAccessSis", user.CanAccessSis.ToString()),
            new("IsAdmin", user.IsAdmin.ToString()),
            new("PibSim", user.PibSim.ToString()),
            new("PibSis", user.PibSis.ToString()),
            new("PebSim", user.PebSim.ToString()),
            new("PebSis", user.PebSis.ToString()),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        await _audit.LogAsync(user.UserName, "SWITCH_ENTITY", "AUTH",
            description: $"Beralih entitas aktif ke {requestedEntity} ({entityName})", ipAddress: GetClientIp());

        TempData["Success"] = $"Berhasil beralih ke entitas {requestedEntity} ({entityName}).";

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "unknown";
        await _audit.LogAsync(username, "LOGOUT", "AUTH", description: "Logout berhasil");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult ChangePassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (model.NewPassword != model.ConfirmPassword)
        {
            ModelState.AddModelError("ConfirmPassword", "Konfirmasi password tidak cocok.");
            return View(model);
        }

        var username = User.Identity?.Name ?? "";
        var result = await _auth.ChangePasswordAsync(username, model.CurrentPassword, model.NewPassword);
        if (!result)
        {
            ModelState.AddModelError("CurrentPassword", "Password saat ini tidak benar.");
            return View(model);
        }

        await _audit.LogAsync(username, "CHANGE_PASSWORD", "AUTH", description: "Password berhasil diubah");
        TempData["Success"] = "Password berhasil diubah.";
        return RedirectToAction("Index", "Dashboard");
    }

    private string GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
