// Models/Auth/UserModel.cs
namespace DoItG2.Models.Auth;

public class UserModel
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string UserType { get; set; } = "STAFF"; // ADMIN, STAFF, SUPERVISOR, VIEWER, KITE
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? LastLogin { get; set; }
    
    // Privileges
    public bool IsAdmin { get; set; }
    public bool IsPartmaster { get; set; }
    public bool IsPI { get; set; }
    public bool IsMatrix { get; set; }
    public bool IsFasilitas { get; set; }
    public bool IsPKB { get; set; }
    
    // PIB Privileges
    public bool PibSim { get; set; } // Key 81
    public bool PibSis { get; set; } // Key 84
    public bool PibAuthorize81 { get; set; }
    public bool PibAuthorize84 { get; set; }
    public bool PibCheck81 { get; set; }
    public bool PibCheck84 { get; set; }
    
    // PEB Privileges
    public bool PebSim { get; set; }
    public bool PebSis { get; set; }
    public bool PebAuthorize81 { get; set; }
    public bool PebAuthorize84 { get; set; }
    public bool PebCheck81 { get; set; }
    public bool PebCheck84 { get; set; }
}

public class LoginViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
