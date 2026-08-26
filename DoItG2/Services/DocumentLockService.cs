using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DoItG2.Data;
using Microsoft.Extensions.Logging;

namespace DoItG2.Services;

public class DocumentLockInfo
{
    public string Car { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string LockedByUser { get; set; } = string.Empty;
    public string LockedByName { get; set; } = string.Empty;
    public string LockedByEntity { get; set; } = string.Empty;
    public DateTime LockedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public bool IsLockedByOther { get; set; }
}

public interface IDocumentLockService
{
    Task<(bool isLocked, string lockedByName, string lockedByUser, DateTime? lockedAt, DateTime? expiresAt)> AcquireLockAsync(string car, string docType, string username, string fullName, string entity);
    Task<(bool isLocked, string lockedByName, string lockedByUser, DateTime? lockedAt, DateTime? expiresAt)> CheckLockAsync(string car, string currentUsername);
    Task<bool> HeartbeatLockAsync(string car, string username);
    Task<bool> ReleaseLockAsync(string car, string username);
    Task<bool> ForceUnlockAsync(string car, string adminUsername);
    Task<List<DocumentLockInfo>> GetActiveLocksAsync();
    Task<(bool isDuplicate, string message)> CheckCarDuplicateAsync(string car, string docType);
}

public class DocumentLockService : IDocumentLockService
{
    private readonly DatabaseContext _db;
    private readonly ILogger<DocumentLockService> _logger;

    public DocumentLockService(DatabaseContext db, ILogger<DocumentLockService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(bool isLocked, string lockedByName, string lockedByUser, DateTime? lockedAt, DateTime? expiresAt)> AcquireLockAsync(
        string car, string docType, string username, string fullName, string entity)
    {
        if (string.IsNullOrWhiteSpace(car))
            return (false, string.Empty, string.Empty, null, null);

        var cleanCar = car.Trim();
        try
        {
            // 1. Clean expired locks first
            await _db.ExecuteAsync("DELETE FROM DOIT_DOCUMENT_LOCK WHERE EXPIRES_AT < GETDATE()");

            // 2. Query active lock for this CAR
            var existingLock = await _db.QueryFirstOrDefaultAsync<DocumentLockInfo>(
                "SELECT CAR, DOC_TYPE AS DocType, LOCKED_BY_USER AS LockedByUser, LOCKED_BY_NAME AS LockedByName, LOCKED_BY_ENTITY AS LockedByEntity, LOCKED_AT AS LockedAt, EXPIRES_AT AS ExpiresAt FROM DOIT_DOCUMENT_LOCK WHERE CAR = @Car",
                new { Car = cleanCar });

            if (existingLock != null)
            {
                // If locked by the same user, extend the lock
                if (string.Equals(existingLock.LockedByUser, username, StringComparison.OrdinalIgnoreCase))
                {
                    await _db.ExecuteAsync(
                        "UPDATE DOIT_DOCUMENT_LOCK SET EXPIRES_AT = DATEADD(MINUTE, 15, GETDATE()), LAST_HEARTBEAT = GETDATE() WHERE CAR = @Car",
                        new { Car = cleanCar });
                    return (false, string.Empty, string.Empty, null, null);
                }

                // If locked by another user and not expired
                if (existingLock.ExpiresAt >= DateTime.Now)
                {
                    return (true, existingLock.LockedByName, existingLock.LockedByUser, existingLock.LockedAt, existingLock.ExpiresAt);
                }
            }

            // 3. Acquire new lock for current user (15 minutes expiry)
            var mergeSql = @"
                IF EXISTS (SELECT 1 FROM DOIT_DOCUMENT_LOCK WHERE CAR = @Car)
                BEGIN
                    UPDATE DOIT_DOCUMENT_LOCK 
                    SET DOC_TYPE = @DocType, LOCKED_BY_USER = @Username, LOCKED_BY_NAME = @FullName, 
                        LOCKED_BY_ENTITY = @Entity, LOCKED_AT = GETDATE(), EXPIRES_AT = DATEADD(MINUTE, 15, GETDATE()), LAST_HEARTBEAT = GETDATE()
                    WHERE CAR = @Car;
                END
                ELSE
                BEGIN
                    INSERT INTO DOIT_DOCUMENT_LOCK (CAR, DOC_TYPE, LOCKED_BY_USER, LOCKED_BY_NAME, LOCKED_BY_ENTITY, LOCKED_AT, EXPIRES_AT, LAST_HEARTBEAT)
                    VALUES (@Car, @DocType, @Username, @FullName, @Entity, GETDATE(), DATEADD(MINUTE, 15, GETDATE()), GETDATE());
                END";

            await _db.ExecuteAsync(mergeSql, new
            {
                Car = cleanCar,
                DocType = docType,
                Username = username,
                FullName = string.IsNullOrWhiteSpace(fullName) ? username : fullName,
                Entity = entity ?? "SIM"
            });

            return (false, string.Empty, string.Empty, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for CAR: {Car}", cleanCar);
            return (false, string.Empty, string.Empty, null, null);
        }
    }

    public async Task<(bool isLocked, string lockedByName, string lockedByUser, DateTime? lockedAt, DateTime? expiresAt)> CheckLockAsync(
        string car, string currentUsername)
    {
        if (string.IsNullOrWhiteSpace(car))
            return (false, string.Empty, string.Empty, null, null);

        var cleanCar = car.Trim();
        try
        {
            var existingLock = await _db.QueryFirstOrDefaultAsync<DocumentLockInfo>(
                "SELECT CAR, DOC_TYPE AS DocType, LOCKED_BY_USER AS LockedByUser, LOCKED_BY_NAME AS LockedByName, LOCKED_BY_ENTITY AS LockedByEntity, LOCKED_AT AS LockedAt, EXPIRES_AT AS ExpiresAt FROM DOIT_DOCUMENT_LOCK WHERE CAR = @Car AND EXPIRES_AT >= GETDATE()",
                new { Car = cleanCar });

            if (existingLock == null || string.Equals(existingLock.LockedByUser, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return (false, string.Empty, string.Empty, null, null);
            }

            return (true, existingLock.LockedByName, existingLock.LockedByUser, existingLock.LockedAt, existingLock.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking lock for CAR: {Car}", cleanCar);
            return (false, string.Empty, string.Empty, null, null);
        }
    }

    public async Task<bool> HeartbeatLockAsync(string car, string username)
    {
        if (string.IsNullOrWhiteSpace(car)) return false;
        var cleanCar = car.Trim();
        try
        {
            var rows = await _db.ExecuteAsync(
                "UPDATE DOIT_DOCUMENT_LOCK SET EXPIRES_AT = DATEADD(MINUTE, 15, GETDATE()), LAST_HEARTBEAT = GETDATE() WHERE CAR = @Car AND LOCKED_BY_USER = @Username",
                new { Car = cleanCar, Username = username });
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extending lock heartbeat for CAR: {Car}", cleanCar);
            return false;
        }
    }

    public async Task<bool> ReleaseLockAsync(string car, string username)
    {
        if (string.IsNullOrWhiteSpace(car)) return false;
        var cleanCar = car.Trim();
        try
        {
            var rows = await _db.ExecuteAsync(
                "DELETE FROM DOIT_DOCUMENT_LOCK WHERE CAR = @Car AND (LOCKED_BY_USER = @Username OR @Username = 'ADMIN')",
                new { Car = cleanCar, Username = username });
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for CAR: {Car}", cleanCar);
            return false;
        }
    }

    public async Task<bool> ForceUnlockAsync(string car, string adminUsername)
    {
        if (string.IsNullOrWhiteSpace(car)) return false;
        var cleanCar = car.Trim();
        try
        {
            var rows = await _db.ExecuteAsync("DELETE FROM DOIT_DOCUMENT_LOCK WHERE CAR = @Car", new { Car = cleanCar });
            _logger.LogInformation("Admin {Admin} force unlocked document CAR: {Car}", adminUsername, cleanCar);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error force unlocking document CAR: {Car}", cleanCar);
            return false;
        }
    }

    public async Task<List<DocumentLockInfo>> GetActiveLocksAsync()
    {
        try
        {
            var sql = @"SELECT CAR, DOC_TYPE AS DocType, LOCKED_BY_USER AS LockedByUser, LOCKED_BY_NAME AS LockedByName, 
                        LOCKED_BY_ENTITY AS LockedByEntity, LOCKED_AT AS LockedAt, EXPIRES_AT AS ExpiresAt, LAST_HEARTBEAT AS LastHeartbeat 
                        FROM DOIT_DOCUMENT_LOCK WHERE EXPIRES_AT >= GETDATE() ORDER BY LOCKED_AT DESC";
            return (await _db.QueryAsync<DocumentLockInfo>(sql)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching active document locks");
            return new List<DocumentLockInfo>();
        }
    }

    public async Task<(bool isDuplicate, string message)> CheckCarDuplicateAsync(string car, string docType)
    {
        if (string.IsNullOrWhiteSpace(car))
            return (false, string.Empty);

        var cleanCar = car.Trim();
        try
        {
            // Check PIB
            if (string.Equals(docType, "PIB", StringComparison.OrdinalIgnoreCase))
            {
                var pibCount = await _db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM PIB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car",
                    new { Car = cleanCar });
                if (pibCount > 0)
                {
                    return (true, $"Nomor Pengajuan (CAR) {cleanCar} sudah terdaftar pada dokumen PIB (BC 2.0). Nomor Aju harus unik dan tidak boleh duplikat.");
                }
            }
            // Check PEB
            else if (string.Equals(docType, "PEB", StringComparison.OrdinalIgnoreCase))
            {
                var pebCount = await _db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM PEB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car",
                    new { Car = cleanCar });
                if (pebCount > 0)
                {
                    return (true, $"Nomor Pengajuan (CAR) {cleanCar} sudah terdaftar pada dokumen PEB (BC 3.0). Nomor Aju harus unik dan tidak boleh duplikat.");
                }
            }
            else
            {
                // Check both
                var pibCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM PIB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car", new { Car = cleanCar });
                if (pibCount > 0) return (true, $"Nomor Pengajuan (CAR) {cleanCar} sudah terdaftar pada dokumen PIB.");

                var pebCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM PEB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car", new { Car = cleanCar });
                if (pebCount > 0) return (true, $"Nomor Pengajuan (CAR) {cleanCar} sudah terdaftar pada dokumen PEB.");
            }

            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking duplicate CAR: {Car}", cleanCar);
            return (false, string.Empty);
        }
    }
}
