using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.Common;
using Microsoft.Extensions.Logging;

namespace DoItG2.Services;

public interface IWorkflowService
{
    Task<bool> SubmitForReviewAsync(string car, string docType, string username, string? notes = null);
    Task<bool> ApproveAsync(string car, string docType, string username, string? notes = null);
    Task<bool> RejectAsync(string car, string docType, string username, string notes);
    Task<IEnumerable<ApprovalLogModel>> GetApprovalHistoryAsync(string car);
}

public class WorkflowService : IWorkflowService
{
    private readonly DatabaseContext _db;
    private readonly IEmailNotificationService _notification;
    private readonly IAuditService _audit;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        DatabaseContext db,
        IEmailNotificationService notification,
        IAuditService audit,
        ILogger<WorkflowService> logger)
    {
        _db = db;
        _notification = notification;
        _audit = audit;
        _logger = logger;
    }

    public async Task<bool> SubmitForReviewAsync(string car, string docType, string username, string? notes = null)
    {
        try
        {
            var isPib = docType.Equals("PIB", StringComparison.OrdinalIgnoreCase);
            var headerTable = isPib ? "PIB_DOIT_FINAL_HEADER" : "PEB_DOIT_FINAL_HEADER";

            var updateSql = $@"UPDATE {headerTable} 
                               SET APPROVAL_STATUS = 'PENDING_APPROVAL', 
                                   SUBMITTED_BY = @User, 
                                   SUBMITTED_DATE = GETDATE(),
                                   REVIEW_NOTES = @Notes
                               WHERE CAR = @Car";

            await _db.ExecuteAsync(updateSql, new { User = username, Notes = notes, Car = car });

            // Record in Approval Log
            await _db.ExecuteAsync(
                @"INSERT INTO DOIT_APPROVAL_LOG (CAR, DOKUMEN_TYPE, PREV_STATUS, NEW_STATUS, ACTION, NOTES, ACTION_BY, ACTION_DATE)
                  VALUES (@Car, @DocType, 'DRAFT', 'PENDING_APPROVAL', 'SUBMIT', @Notes, @User, GETDATE())",
                new { Car = car, DocType = docType.ToUpper(), Notes = notes, User = username });

            await _audit.LogAsync(username, "SUBMIT_APPROVAL", docType.ToUpper(), car, $"Dokumen diajukan untuk review internal oleh {username}");
            await _notification.NotifyDocumentStatusChangeAsync(docType.ToUpper(), car, "MENUNGGU REVIEW", notes ?? "Dokumen diajukan untuk persetujuan supervisor");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting document {Car} for review", car);
            return false;
        }
    }

    public async Task<bool> ApproveAsync(string car, string docType, string username, string? notes = null)
    {
        try
        {
            var isPib = docType.Equals("PIB", StringComparison.OrdinalIgnoreCase);
            var headerTable = isPib ? "PIB_DOIT_FINAL_HEADER" : "PEB_DOIT_FINAL_HEADER";

            var updateSql = $@"UPDATE {headerTable} 
                               SET APPROVAL_STATUS = 'APPROVED', 
                                   APPROVED_BY = @User, 
                                   APPROVED_DATE = GETDATE(),
                                   REVIEW_NOTES = @Notes
                               WHERE CAR = @Car";

            await _db.ExecuteAsync(updateSql, new { User = username, Notes = notes, Car = car });

            // Record in Approval Log
            await _db.ExecuteAsync(
                @"INSERT INTO DOIT_APPROVAL_LOG (CAR, DOKUMEN_TYPE, PREV_STATUS, NEW_STATUS, ACTION, NOTES, ACTION_BY, ACTION_DATE)
                  VALUES (@Car, @DocType, 'PENDING_APPROVAL', 'APPROVED', 'APPROVE', @Notes, @User, GETDATE())",
                new { Car = car, DocType = docType.ToUpper(), Notes = notes, User = username });

            await _audit.LogAsync(username, "APPROVE_INTERNAL", docType.ToUpper(), car, $"Dokumen disetujui oleh Supervisor {username}. Siap kirim ke CEISA 4.0.");
            await _notification.NotifyDocumentStatusChangeAsync(docType.ToUpper(), car, "DISETUJUI (APPROVED)", notes ?? "Dokumen telah disetujui secara internal dan siap dikirim ke Bea Cukai.");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving document {Car}", car);
            return false;
        }
    }

    public async Task<bool> RejectAsync(string car, string docType, string username, string notes)
    {
        try
        {
            var isPib = docType.Equals("PIB", StringComparison.OrdinalIgnoreCase);
            var headerTable = isPib ? "PIB_DOIT_FINAL_HEADER" : "PEB_DOIT_FINAL_HEADER";

            var updateSql = $@"UPDATE {headerTable} 
                               SET APPROVAL_STATUS = 'REJECTED', 
                                   REVIEW_NOTES = @Notes
                               WHERE CAR = @Car";

            await _db.ExecuteAsync(updateSql, new { Notes = notes, Car = car });

            // Record in Approval Log
            await _db.ExecuteAsync(
                @"INSERT INTO DOIT_APPROVAL_LOG (CAR, DOKUMEN_TYPE, PREV_STATUS, NEW_STATUS, ACTION, NOTES, ACTION_BY, ACTION_DATE)
                  VALUES (@Car, @DocType, 'PENDING_APPROVAL', 'REJECTED', 'REJECT', @Notes, @User, GETDATE())",
                new { Car = car, DocType = docType.ToUpper(), Notes = notes, User = username });

            await _audit.LogAsync(username, "REJECT_INTERNAL", docType.ToUpper(), car, $"Dokumen ditolak / perlu revisi: {notes}", isError: true);
            await _notification.NotifyDocumentStatusChangeAsync(docType.ToUpper(), car, "PERLU REVISI (REJECTED)", notes);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting document {Car}", car);
            return false;
        }
    }

    public async Task<IEnumerable<ApprovalLogModel>> GetApprovalHistoryAsync(string car)
    {
        try
        {
            var sql = @"SELECT ID AS Id, CAR AS Car, DOKUMEN_TYPE AS DokumenType, PREV_STATUS AS PrevStatus,
                               NEW_STATUS AS NewStatus, ACTION AS Action, NOTES AS Notes, 
                               ACTION_BY AS ActionBy, ACTION_DATE AS ActionDate
                        FROM DOIT_APPROVAL_LOG
                        WHERE CAR = @Car
                        ORDER BY ACTION_DATE DESC";
            return await _db.QueryAsync<ApprovalLogModel>(sql, new { Car = car });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approval history for {Car}", car);
            return new List<ApprovalLogModel>();
        }
    }
}
