using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.Common;
using DoItG2.Models.PEB;
using DoItG2.Models.PIB;
using DoItG2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoItG2.Controllers;

[Authorize]
public class ApprovalController : Controller
{
    private readonly DatabaseContext _db;
    private readonly IWorkflowService _workflow;

    public ApprovalController(DatabaseContext db, IWorkflowService workflow)
    {
        _db = db;
        _workflow = workflow;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string tab = "pending")
    {
        ViewData["Title"] = "Pusat Persetujuan Dokumen (Approval Center)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Pusat Persetujuan";

        var entity = User.FindFirst("Entity")?.Value ?? "SIM";

        // 1. Query PIB Documents with NOLOCK
        var pibSql = @"SELECT h.CAR, h.ID_IMP AS IdImp, h.NM_IMO AS NmImo, h.NM_PEMASOK AS NmPemasok, 
                              h.TGL_TIBA AS TglTiba, h.JML_BRG AS JmlBrg, 
                              ISNULL(h.APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                              ISNULL(h.ENTITY, 'SIM') AS Entity,
                              h.KD_VAL AS KdVal, h.CIF AS Cif,
                              h.SUBMITTED_BY AS SubmittedBy, h.SUBMITTED_DATE AS SubmittedDate,
                              h.APPROVED_BY AS ApprovedBy, h.APPROVED_DATE AS ApprovedDate,
                              h.REVIEW_NOTES AS ReviewNotes,
                              h.NO_PEN_PIB AS PibNo, h.NO_SPPB AS SppbNo
                       FROM PIB_DOIT_FINAL_HEADER h WITH (NOLOCK)
                       WHERE (h.ENTITY = @Entity OR (@Entity = 'SIM' AND (h.ENTITY IS NULL OR h.NM_IMO LIKE '%MOTOR%' OR h.ID_IMP LIKE '%011297371%')) OR (@Entity = 'SIS' AND (h.ENTITY = 'SIS' OR h.NM_IMO LIKE '%SALES%' OR h.ID_IMP LIKE '%011297389%')))
                       ORDER BY h.CREATION_DATE DESC";
        var allPibs = (await _db.QueryAsync<PibHeaderModel>(pibSql, new { Entity = entity })).ToList();

        // 2. Query PEB Documents with NOLOCK
        var pebSql = @"SELECT h.CAR, h.NAMAEKS AS NamaBeli, h.TGEKS AS TgEks, h.NETTO, h.BRUTO, h.FOB,
                              ISNULL(h.APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                              ISNULL(h.ENTITY, 'SIM') AS Entity,
                              h.SUBMITTED_BY AS SubmittedBy, h.SUBMITTED_DATE AS SubmittedDate,
                              h.APPROVED_BY AS ApprovedBy, h.APPROVED_DATE AS ApprovedDate,
                              h.REVIEW_NOTES AS ReviewNotes,
                              h.NOPEN AS Nopen, h.NONPE AS Snrf, h.NEGBELI AS NegBeli
                       FROM PEB_DOIT_FINAL_HEADER h WITH (NOLOCK)
                       WHERE (h.ENTITY = @Entity OR (@Entity = 'SIM' AND (h.ENTITY IS NULL OR h.NAMAEKS LIKE '%MOTOR%' OR h.NPWPEKS LIKE '%011297371%')) OR (@Entity = 'SIS' AND (h.ENTITY = 'SIS' OR h.NAMAEKS LIKE '%SALES%' OR h.NPWPEKS LIKE '%011297389%')))
                       ORDER BY h.CREATED_DATE DESC";
        var allPebs = (await _db.QueryAsync<PebHeaderModel>(pebSql, new { Entity = entity })).ToList();

        // 3. Query Recent Logs with NOLOCK
        var logSql = @"SELECT TOP 50 ID AS Id, CAR AS Car, DOKUMEN_TYPE AS DokumenType, PREV_STATUS AS PrevStatus,
                              NEW_STATUS AS NewStatus, ACTION AS Action, NOTES AS Notes, 
                              ACTION_BY AS ActionBy, ACTION_DATE AS ActionDate
                       FROM DOIT_APPROVAL_LOG WITH (NOLOCK)
                       ORDER BY ACTION_DATE DESC";
        var recentLogs = (await _db.QueryAsync<ApprovalLogModel>(logSql)).ToList();

        var vm = new ApprovalDashboardViewModel
        {
            ActiveTab = tab.ToLowerInvariant(),
            PendingPibList = allPibs.Where(p => p.ApprovalStatus == "PENDING_APPROVAL" || p.ApprovalStatus == "PENDING").ToList(),
            PendingPebList = allPebs.Where(p => p.ApprovalStatus == "PENDING_APPROVAL" || p.ApprovalStatus == "PENDING").ToList(),
            ApprovedPibList = allPibs.Where(p => p.ApprovalStatus == "APPROVED" || p.ApprovalStatus == "TRANSMITTED").ToList(),
            ApprovedPebList = allPebs.Where(p => p.ApprovalStatus == "APPROVED" || p.ApprovalStatus == "TRANSMITTED").ToList(),
            RejectedPibList = allPibs.Where(p => p.ApprovalStatus == "REJECTED" || p.ApprovalStatus == "FAILED").ToList(),
            RejectedPebList = allPebs.Where(p => p.ApprovalStatus == "REJECTED" || p.ApprovalStatus == "FAILED").ToList(),
            RecentLogs = recentLogs
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string car, string docType, string? notes)
    {
        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.ApproveAsync(car, docType, username, notes);
        if (success)
            TempData["Success"] = $"Dokumen {docType} ({car}) berhasil disetujui (Approved)! Dokumen siap dikirim ke CEISA 4.0.";
        else
            TempData["Error"] = $"Gagal menyetujui dokumen {docType} ({car}).";

        return RedirectToAction(nameof(Index), new { tab = "pending" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(string car, string docType, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["Error"] = "Catatan perbaikan / alasan revisi wajib diisi agar operator tahu apa yang harus diperbaiki.";
            return RedirectToAction(nameof(Index), new { tab = "pending" });
        }

        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.RejectAsync(car, docType, username, notes);
        if (success)
            TempData["Success"] = $"Dokumen {docType} ({car}) telah dikembalikan ke operator dengan instruksi revisi.";
        else
            TempData["Error"] = $"Gagal memproses penolakan dokumen {docType} ({car}).";

        return RedirectToAction(nameof(Index), new { tab = "pending" });
    }
}
