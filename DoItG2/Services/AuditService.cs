using DoItG2.Data;
using DoItG2.Models.Common;

namespace DoItG2.Services;

public interface IAuditService
{
    Task LogAsync(string username, string action, string module,
        string? documentId = null, string? description = null,
        string? ipAddress = null, bool isError = false);
    Task<List<AuditLogModel>> GetLogsAsync(int page = 1, int pageSize = 50, string? module = null);
}

public class AuditService : IAuditService
{
    private readonly DatabaseContext _db;
    public AuditService(DatabaseContext db) => _db = db;

    public async Task LogAsync(string username, string action, string module,
        string? documentId = null, string? description = null,
        string? ipAddress = null, bool isError = false)
    {
        try
        {
            await _db.ExecuteAsync(@"
                INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, is_error, created_at)
                VALUES (@UserName, @Action, @Module, @DocumentId, @Description, @IpAddress, @IsError, GETDATE())",
                new { UserName = username, Action = action, Module = module, DocumentId = documentId, Description = description, IpAddress = ipAddress, IsError = isError });
        }
        catch { /* Audit logging should never break the main flow */ }
    }

    public async Task<List<AuditLogModel>> GetLogsAsync(int page = 1, int pageSize = 50, string? module = null)
    {
        var where = module != null ? "WHERE module = @Module" : "";
        var sql = $@"SELECT id, user_name AS UserName, action, module, document_id AS DocumentId,
                    description, ip_address AS IpAddress, is_error AS IsError, created_at AS CreatedAt
                    FROM doit_audit_log {where}
                    ORDER BY created_at DESC
                    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        return (await _db.QueryAsync<AuditLogModel>(sql,
            new { Module = module, Skip = (page - 1) * pageSize, Take = pageSize })).ToList();
    }
}
