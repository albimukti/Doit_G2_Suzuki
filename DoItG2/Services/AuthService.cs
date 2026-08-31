using DoItG2.Data;
using DoItG2.Models.Auth;

namespace DoItG2.Services;

public interface IAuthService
{
    Task<UserModel?> ValidateUserAsync(string username, string password);
    Task<UserModel?> GetUserByUsernameAsync(string username);
    Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword);
    Task<List<UserModel>> GetAllUsersAsync();
    Task<int> CreateUserAsync(UserModel user, string password);
    Task<bool> UpdateUserAsync(UserModel user);
    Task<bool> DeactivateUserAsync(int userId);
}

public class AuthService : IAuthService
{
    private readonly DatabaseContext _db;
    private readonly ILogger<AuthService> _logger;

    public AuthService(DatabaseContext db, ILogger<AuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserModel?> ValidateUserAsync(string username, string password)
    {
        try
        {
            var user = await GetUserByUsernameAsync(username);
            if (user == null || !user.IsActive) return null;
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;

            // Update last login
            await _db.ExecuteAsync(
                "UPDATE doit_user SET last_login = GETDATE() WHERE user_name = @UserName",
                new { UserName = username });
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user: {Username}", username);
            return null;
        }
    }

    public async Task<UserModel?> GetUserByUsernameAsync(string username)
    {
        var sql = @"SELECT id, user_name AS UserName, full_name AS FullName, email,
                    password_hash AS PasswordHash, user_type AS UserType, is_active AS IsActive,
                    created_date AS CreatedDate, last_login AS LastLogin,
                    ISNULL(entity_access, 'ALL') AS EntityAccess,
                    is_admin AS IsAdmin, is_partmaster AS IsPartmaster, is_pi AS IsPI,
                    is_matrix AS IsMatrix, is_fasilitas AS IsFasilitas, is_pkb AS IsPKB,
                    pib_sim AS PibSim, pib_sis AS PibSis, pib_authorize_81 AS PibAuthorize81,
                    pib_authorize_84 AS PibAuthorize84, pib_check_81 AS PibCheck81, pib_check_84 AS PibCheck84,
                    peb_sim AS PebSim, peb_sis AS PebSis, peb_authorize_81 AS PebAuthorize81,
                    peb_authorize_84 AS PebAuthorize84, peb_check_81 AS PebCheck81, peb_check_84 AS PebCheck84
                    FROM doit_user WHERE user_name = @UserName";
        return await _db.QueryFirstOrDefaultAsync<UserModel>(sql, new { UserName = username });
    }

    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await ValidateUserAsync(username, currentPassword);
        if (user == null) return false;
        var hash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        var rows = await _db.ExecuteAsync(
            "UPDATE doit_user SET password_hash = @Hash WHERE user_name = @UserName",
            new { Hash = hash, UserName = username });
        return rows > 0;
    }

    public async Task<List<UserModel>> GetAllUsersAsync()
    {
        var sql = @"SELECT id, user_name AS UserName, full_name AS FullName, email,
                    user_type AS UserType, is_active AS IsActive, created_date AS CreatedDate, last_login AS LastLogin,
                    ISNULL(entity_access, 'ALL') AS EntityAccess
                    FROM doit_user ORDER BY full_name";
        return (await _db.QueryAsync<UserModel>(sql)).ToList();
    }

    public async Task<int> CreateUserAsync(UserModel user, string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var entityAccess = string.IsNullOrWhiteSpace(user.EntityAccess) ? "ALL" : user.EntityAccess.ToUpper();
        var sql = @"INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type,
                    is_active, entity_access, is_admin, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb,
                    pib_sim, pib_sis, pib_authorize_81, pib_authorize_84, pib_check_81, pib_check_84,
                    peb_sim, peb_sis, peb_authorize_81, peb_authorize_84, peb_check_81, peb_check_84,
                    created_date)
                    VALUES (@UserName, @FullName, @Email, @Hash, @UserType,
                    @IsActive, @EntityAccess, @IsAdmin, @IsPartmaster, @IsPI, @IsMatrix, @IsFasilitas, @IsPKB,
                    @PibSim, @PibSis, @PibAuthorize81, @PibAuthorize84, @PibCheck81, @PibCheck84,
                    @PebSim, @PebSis, @PebAuthorize81, @PebAuthorize84, @PebCheck81, @PebCheck84, GETDATE());
                    SELECT SCOPE_IDENTITY();";
        return await _db.ExecuteScalarAsync<int>(sql, new
        {
            user.UserName, user.FullName, user.Email, Hash = hash, user.UserType,
            user.IsActive, EntityAccess = entityAccess, user.IsAdmin, user.IsPartmaster, user.IsPI, user.IsMatrix,
            user.IsFasilitas, user.IsPKB, user.PibSim, user.PibSis,
            user.PibAuthorize81, user.PibAuthorize84, user.PibCheck81, user.PibCheck84,
            user.PebSim, user.PebSis, user.PebAuthorize81, user.PebAuthorize84,
            user.PebCheck81, user.PebCheck84
        });
    }

    public async Task<bool> UpdateUserAsync(UserModel user)
    {
        var entityAccess = string.IsNullOrWhiteSpace(user.EntityAccess) ? "ALL" : user.EntityAccess.ToUpper();
        var sql = @"UPDATE doit_user SET full_name=@FullName, email=@Email, user_type=@UserType,
                    is_active=@IsActive, entity_access=@EntityAccess, is_admin=@IsAdmin, is_partmaster=@IsPartmaster,
                    is_pi=@IsPI, is_matrix=@IsMatrix, is_fasilitas=@IsFasilitas, is_pkb=@IsPKB,
                    pib_sim=@PibSim, pib_sis=@PibSis, pib_authorize_81=@PibAuthorize81,
                    pib_authorize_84=@PibAuthorize84, pib_check_81=@PibCheck81, pib_check_84=@PibCheck84,
                    peb_sim=@PebSim, peb_sis=@PebSis, peb_authorize_81=@PebAuthorize81,
                    peb_authorize_84=@PebAuthorize84, peb_check_81=@PebCheck81, peb_check_84=@PebCheck84
                    WHERE id=@Id";
        return await _db.ExecuteAsync(sql, new
        {
            user.FullName, user.Email, user.UserType,
            user.IsActive, EntityAccess = entityAccess, user.IsAdmin, user.IsPartmaster,
            user.IsPI, user.IsMatrix, user.IsFasilitas, user.IsPKB,
            user.PibSim, user.PibSis, user.PibAuthorize81,
            user.PibAuthorize84, user.PibCheck81, user.PibCheck84,
            user.PebSim, user.PebSis, user.PebAuthorize81,
            user.PebAuthorize84, user.PebCheck81, user.PebCheck84,
            user.Id
        }) > 0;
    }

    public async Task<bool> DeactivateUserAsync(int userId)
    {
        return await _db.ExecuteAsync(
            "UPDATE doit_user SET is_active = 0 WHERE id = @Id", new { Id = userId }) > 0;
    }
}
