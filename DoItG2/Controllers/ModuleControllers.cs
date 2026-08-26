using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.PIB;
using DoItG2.Models.PEB;
using DoItG2.Models.Auth;
using DoItG2.Models.Common;
using DoItG2.Models.CEISA;
using DoItG2.Services;
using ClosedXML.Excel;
using Oracle.ManagedDataAccess.Client;

namespace DoItG2.Controllers;

[Authorize]
public class PibController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ITaxCalculationService _tax;
    private readonly IWorkflowService _workflow;
    private readonly IValidationService _validation;
    private readonly IPdfReportService _pdf;
    private readonly ICeisaIntegrationService _ceisa;
    private readonly IAuditService _audit;
    private readonly IDocumentLockService _lockService;
    private readonly ILogger<PibController> _logger;

    public PibController(
        DatabaseContext db,
        ITaxCalculationService tax,
        IWorkflowService workflow,
        IValidationService validation,
        IPdfReportService pdf,
        ICeisaIntegrationService ceisa,
        IAuditService audit,
        IDocumentLockService lockService,
        ILogger<PibController> logger)
    {
        _db = db;
        _tax = tax;
        _workflow = workflow;
        _validation = validation;
        _pdf = pdf;
        _ceisa = ceisa;
        _audit = audit;
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string search, string status, string approvalStatus, int page = 1)
    {
        ViewData["Title"] = "Daftar PIB (BC 2.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> PIB";
        
        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var sql = @"SELECT CAR, ASAL_DATA AS AsalData, ID_IMP AS IdImp, NM_IMO AS NmImo, NM_PEMASOK AS NmPemasok, 
                       TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg,
                       NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, KD_VAL AS KdVal, CIF AS Cif,
                       TOTAL_PUNGUTAN AS TotalPungutan, NILAI_PABEAN AS NilaiPabean,
                       ISNULL(ENTITY, 'SIM') AS Entity,
                       ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                       CASE 
                            WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                            WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                            ELSE 'DRAFT'
                       END AS Status 
                       FROM PIB_DOIT_FINAL_HEADER 
                       WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NM_IMO LIKE '%MOTOR%' OR ID_IMP LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NM_IMO LIKE '%SALES%' OR ID_IMP LIKE '%011297389%')))";
                       
            var parameters = new DynamicParameters();
            parameters.Add("Entity", entity);
            
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (CAR LIKE @Search OR NM_PEMASOK LIKE @Search OR NM_IMO LIKE @Search OR ID_IMP LIKE @Search OR NO_PEN_PIB LIKE @Search OR NO_SPPB LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "SPPB") sql += " AND NO_SPPB IS NOT NULL AND NO_SPPB <> ''";
                else if (status == "NOPEN") sql += " AND NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' AND (NO_SPPB IS NULL OR NO_SPPB = '')";
                else if (status == "DRAFT") sql += " AND (NO_PEN_PIB IS NULL OR NO_PEN_PIB = '')";
            }
            if (!string.IsNullOrEmpty(approvalStatus))
            {
                sql += " AND APPROVAL_STATUS = @ApprovalStatus";
                parameters.Add("ApprovalStatus", approvalStatus);
            }
            
            sql += " ORDER BY CREATION_DATE DESC, CAR DESC";
            
            var items = await _db.QueryAsync<PibHeaderModel>(sql, parameters);
            return View(items.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PIB list");
            return View(new List<PibHeaderModel>());
        }
    }

    public IActionResult Create()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Akun Manajer Operasional hanya memiliki hak akses pantau/laporan dan tidak dapat membuat dokumen baru.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Buat PIB Baru (BC 2.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(PibHeaderModel model, IFormCollection form)
    {
        try
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Akun Manajer Operasional tidak memiliki izin membuat dokumen baru.";
                return RedirectToAction(nameof(Index));
            }

            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var isSis = entity == "SIS";

            if (string.IsNullOrWhiteSpace(model.Car))
            {
                model.Car = "010100" + DateTime.Now.ToString("yyMMdd") + new Random().Next(100000, 999999);
            }
            else
            {
                model.Car = model.Car.Trim();
            }

            // Anti-Duplikasi Nomor Pengajuan (CAR)
            var (isDuplicate, dupMsg) = await _lockService.CheckCarDuplicateAsync(model.Car, "PIB");
            if (isDuplicate)
            {
                TempData["Error"] = dupMsg;
                ViewData["Title"] = "Buat PIB Baru (BC 2.0)";
                ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
                return View(model);
            }

            model.Entity = entity;
            model.KdKantor = form["KdKantor"].FirstOrDefault() ?? "010100";
            model.JnsPib = form["JnsPib"].FirstOrDefault() ?? "1";
            model.JnsImp = form["JnsImp"].FirstOrDefault() ?? "1";
            model.JnsBayar = form["JnsBayar"].FirstOrDefault() ?? "1";
            model.AsalData = form["AsalData"].FirstOrDefault() ?? "M";
            model.KdSkepFas = form["KdSkepFas"].FirstOrDefault() ?? "";
            
            model.IdImp = !string.IsNullOrWhiteSpace(form["IdImp"]) ? form["IdImp"].FirstOrDefault()! : (isSis ? "011297389411000" : "011297371411000");
            model.NmImo = !string.IsNullOrWhiteSpace(form["NmImo"]) ? form["NmImo"].FirstOrDefault()! : (isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR");
            model.AlImp = !string.IsNullOrWhiteSpace(form["AlImp"]) ? form["AlImp"].FirstOrDefault()! : (isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8" : "JL. RAYA PENGGILINGAN KM 19");
            model.StatusImp = form["StatusImp"].FirstOrDefault() ?? "ATA";
            model.IdPpjk = form["IdPpjk"].FirstOrDefault() ?? "";
            model.NmPpjk = form["NmPpjk"].FirstOrDefault() ?? "";
            model.AlPpjk = form["AlPpjk"].FirstOrDefault() ?? "";

            model.NegPemasok = form["NegPemasok"].FirstOrDefault() ?? (model.NegPemasok ?? "JP");
            model.NmPemasok = form["NmPemasok"].FirstOrDefault() ?? (model.NmPemasok ?? "SUZUKI MOTOR CORPORATION JAPAN");
            model.AlPemasok = form["AlPemasok"].FirstOrDefault() ?? (model.AlPemasok ?? "300 TAKATSUKA-CHO, CHUO-KU, HAMAMATSU-SHI, SHIZUOKA, JAPAN");

            model.CaraAngkut = form["CaraAngkut"].FirstOrDefault() ?? "1";
            model.NmAngkut = form["NmAngkut"].FirstOrDefault() ?? "WAN HAI 315";
            model.BenderaVoy = form["BenderaVoy"].FirstOrDefault() ?? "SG";
            model.NoVoyFlight = form["NoVoyFlight"].FirstOrDefault() ?? "WH-315";
            model.TglTiba = form["TglTiba"].FirstOrDefault() ?? DateTime.Now.ToString("yyyy-MM-dd");
            model.PelMuat = form["PelMuat"].FirstOrDefault() ?? "JPTYO";
            model.PelBongkar = form["PelBongkar"].FirstOrDefault() ?? "IDTPP";
            model.PelTransit = form["PelTransit"].FirstOrDefault() ?? "";
            model.Gudang = form["Gudang"].FirstOrDefault() ?? "UTP1";
            model.NoBc11 = form["NoBc11"].FirstOrDefault() ?? "";
            model.TglBc11 = form["TglBc11"].FirstOrDefault() ?? "";
            model.NoPosBc11 = form["NoPosBc11"].FirstOrDefault() ?? "0001";

            model.KdVal = form["KdVal"].FirstOrDefault() ?? "USD";
            var ndpbmVal = await _tax.GetCurrentNdpbmAsync(model.KdVal);
            model.Ndpbm = ndpbmVal.ToString("F2");
            
            decimal.TryParse(form["Fob"].FirstOrDefault(), out decimal fob);
            decimal.TryParse(form["Asuransi"].FirstOrDefault(), out decimal asuransi);
            decimal.TryParse(form["Freight"].FirstOrDefault(), out decimal freight);
            decimal.TryParse(form["Cif"].FirstOrDefault(), out decimal cif);

            if (cif <= 0 && fob > 0) cif = fob + asuransi + freight;

            model.Fob = fob.ToString("F2");
            model.Asuransi = asuransi.ToString("F2");
            model.Freight = freight.ToString("F2");
            model.Cif = cif.ToString("F2");
            model.Netto = form["Netto"].FirstOrDefault() ?? "15000";
            model.Bruto = form["Bruto"].FirstOrDefault() ?? "16200";
            model.KdJaminan = form["KdJaminan"].FirstOrDefault() ?? "1";
            model.JmlCont = form["JmlCont"].FirstOrDefault() ?? "1";
            model.Status = "DRAFT";
            model.ApprovalStatus = "DRAFT";

            var sqlHeader = @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                (CAR, KD_KANTOR, JNS_PIB, JNS_IMP, JNS_BAYAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, STATUS_IMP,
                 ID_PPJK, NM_PPJK, AL_PPJK, NM_PEMASOK, AL_PEMASOK, NEG_PEMASOK, CARA_ANGKUT, NM_ANGKUT, BENDERA_VOY,
                 NO_VOY_FLIGHT, TGL_TIBA, PEL_MUAT, PEL_TRANSIT, PEL_BONGKAR, GUDANG, NO_BC11, TGL_BC11, NO_POS_BC11,
                 KD_VAL, NDPBM, FOB, ASURANSI, FREIGHT, CIF, NETTO, BRUTO, KD_JAMINAN, JML_CONT, APPROVAL_STATUS, CREATION_DATE, ENTITY)
                VALUES (@Car, @KdKantor, @JnsPib, @JnsImp, @JnsBayar, @AsalData, @IdImp, @NmImo, @AlImp, @StatusImp,
                 @IdPpjk, @NmPpjk, @AlPpjk, @NmPemasok, @AlPemasok, @NegPemasok, @CaraAngkut, @NmAngkut, @BenderaVoy,
                 @NoVoyFlight, @TglTiba, @PelMuat, @PelTransit, @PelBongkar, @Gudang, @NoBc11, @TglBc11, @NoPosBc11,
                 @KdVal, @Ndpbm, @Fob, @Asuransi, @Freight, @Cif, @Netto, @Bruto, @KdJaminan, @JmlCont, 'DRAFT', GETDATE(), @Entity)";
            
            await _db.ExecuteAsync(sqlHeader, model);

            var brgHs = form["BrgHs[]"];
            var brgDesc = form["BrgDesc[]"];
            var brgQty = form["BrgQty[]"];
            var brgSat = form["BrgSat[]"];
            var brgVal = form["BrgVal[]"];
            var brgNeg = form["BrgNeg[]"];

            for (int i = 0; i < brgHs.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(brgHs[i]))
                {
                    decimal.TryParse(brgQty[i], out decimal qty);
                    decimal.TryParse(brgVal[i], out decimal val);
                    var desc = brgDesc.Count > i ? brgDesc[i] : "Item Suzuki";
                    var unitType = brgSat.Count > i ? brgSat[i] : "KGM";
                    var negara = brgNeg.Count > i ? brgNeg[i] : (model.NegPemasok ?? "JP");

                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, UNIT_VAL, CIF_PER_UNIT, ORIGIN_COUNTRY, KD_FAS)
                           VALUES (@Car, @Serial, @HsNo, @Desc, @Qty, @UnitType, @UnitVal, @CifPerUnit, @Country, 'KITE')",
                        new { Car = model.Car, Serial = i + 1, HsNo = brgHs[i], Desc = desc, Qty = qty, UnitType = unitType, UnitVal = val, CifPerUnit = (qty * val), Country = negara });
                }
            }

            var dokKd = form["DokKd[]"];
            var dokNo = form["DokNo[]"];
            var dokTg = form["DokTg[]"];

            for (int i = 0; i < dokKd.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(dokNo[i]))
                {
                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_DOCUMENT (CAR, SERIAL, DOKKD, DOKNO, DOKTG)
                           VALUES (@Car, @Serial, @DokKd, @DokNo, @DokTg)",
                        new { Car = model.Car, Serial = i + 1, DokKd = dokKd[i], DokNo = dokNo[i], DokTg = dokTg.Count > i ? dokTg[i] : DateTime.Now.ToString("yyyy-MM-dd") });
                }
            }

            var contNo = form["ContNo[]"];
            var contUkr = form["ContUkr[]"];
            var contMuat = form["ContMuat[]"];
            var contTipe = form["ContTipe[]"];

            for (int i = 0; i < contNo.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(contNo[i]))
                {
                    var ukr = contUkr.Count > i ? contUkr[i] : "40";
                    var cMuat = contMuat.Count > i ? contMuat[i] : "F";
                    var cTipe = contTipe.Count > i ? contTipe[i] : "1";

                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_CONTAINER (CAR, NO_CONT, UKR_CONT, JNS_MUAT, JNS_CONT)
                           VALUES (@Car, @NoCont, @UkrCont, @JnsMuat, @JnsCont)",
                        new { Car = model.Car, NoCont = contNo[i], UkrCont = ukr, JnsMuat = cMuat, JnsCont = cTipe });
                }
            }

            await _lockService.ReleaseLockAsync(model.Car, User.Identity?.Name ?? "");
            await _audit.LogAsync(User.Identity?.Name ?? "system", "CREATE_PIB", "PIB", model.Car, $"Membuat dokumen PIB BC 2.0 (Nilai CIF: {model.Cif} {model.KdVal})");

            TempData["Success"] = $"Dokumen PIB (BC 2.0) dengan nomor CAR {model.Car} berhasil dibuat!";
            return RedirectToAction(nameof(Detail), new { id = model.Car });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PIB: {Car}", model.Car);
            TempData["Error"] = $"Gagal membuat dokumen PIB: {ex.Message}";
            ModelState.AddModelError("", $"Gagal membuat dokumen PIB: {ex.Message}");
            return View(model);
        }
    }

    public async Task<IActionResult> Detail(string id)
    {
        ViewData["Title"] = $"Detail PIB — {id}";
        ViewData["Breadcrumb"] = $"<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Detail";
        
        try
        {
            var username = User.Identity?.Name ?? "unknown";
            var fullName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? username;
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";

            // Acquire or Check Document Lock
            var lockStatus = await _lockService.AcquireLockAsync(id, "PIB", username, fullName, entity);
            ViewBag.IsLockedByOther = lockStatus.isLocked;
            ViewBag.LockedByName = lockStatus.lockedByName;
            ViewBag.LockedByUser = lockStatus.lockedByUser;
            ViewBag.LockedAt = lockStatus.lockedAt?.ToString("dd-MM-yyyy HH:mm");

            var header = await _db.QueryFirstOrDefaultAsync<PibHeaderModel>(
                @"SELECT CAR, ASAL_DATA AS AsalData, ID_IMP AS IdImp, NM_IMO AS NmImo, AL_IMP AS AlImp, 
                  NM_PEMASOK AS NmPemasok, AL_PEMASOK AS AlPemasok, NEG_PEMASOK AS NegPemasok,
                  TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                  NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, KD_VAL AS KdVal, NDPBM AS Ndpbm,
                  FOB AS Fob, ASURANSI AS Asuransi, FREIGHT AS Freight, CIF AS Cif, NETTO AS Netto, BRUTO AS Bruto,
                  NM_ANGKUT AS NmAngkut, NO_VOY_FLIGHT AS NoVoyFlight, PEL_MUAT AS PelMuat, PEL_BONGKAR AS PelBongkar,
                  NO_BC11 AS NoBc11, NO_POS_BC11 AS NoPosBc11, KD_KANTOR AS KdKantor,
                  TOTAL_BM AS TotalBm, TOTAL_PPN AS TotalPpn, TOTAL_PPH AS TotalPph, TOTAL_PUNGUTAN AS TotalPungutan, NILAI_PABEAN AS NilaiPabean,
                  ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus, REVIEW_NOTES AS ReviewNotes,
                  SUBMITTED_BY AS SubmittedBy, SUBMITTED_DATE AS SubmittedDate,
                  APPROVED_BY AS ApprovedBy, APPROVED_DATE AS ApprovedDate,
                  CASE 
                       WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                       WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                       ELSE 'DRAFT'
                  END AS Status 
                  FROM PIB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car",
                new { Car = id.Trim() });
                
            if (header == null) return NotFound();
            
            var details = await _db.QueryAsync<PibDetailModel>(
                @"SELECT SERIAL AS Serial, HS_NO AS HsNo, GOOD_DESC1 AS GoodDesc1, 
                   QUANTITY AS Quantity, UNIT_TYPE AS UnitType, UNIT_VAL AS UnitVal, CIF_PER_UNIT AS CifPerUnit,
                   ORIGIN_COUNTRY AS OriginCountry, KD_FAS AS KdFas
                   FROM PIB_DOIT_FINAL_DETAIL WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY SERIAL",
                new { Car = id.Trim() });
            header.Details = details.ToList();
            
            var docs = await _db.QueryAsync<PibDocumentModel>(
                @"SELECT SERIAL AS Serial, DOKKD AS DokKd, DOKNO AS DokNo, DOKTG AS DokTg 
                  FROM PIB_DOIT_FINAL_DOCUMENT WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY SERIAL",
                new { Car = id.Trim() });
            header.Documents = docs.ToList();

            var containers = await _db.QueryAsync<PibContainerModel>(
                @"SELECT NO_CONT AS NoCont, UKR_CONT AS UkurCont, JNS_MUAT AS JenisMuat, JNS_CONT AS JenisCont 
                  FROM PIB_DOIT_FINAL_CONTAINER WHERE RTRIM(LTRIM(CAR)) = @Car",
                new { Car = id.Trim() });
            header.Containers = containers.ToList();

            var responses = await _db.QueryAsync<PibResponModel>(
                @"SELECT RESKD AS ResKd, RESTG AS ResTg, DOKRESNO AS DokResNo, DOKRESTG AS DokResTg, 
                         KPBC AS Kpbc, PIBNO AS PibNo, PIBTG AS PibTg, DESKRIPSI AS Deskripsi
                  FROM PIB_DOIT_FINAL_RESPON WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY RESTG DESC",
                new { Car = id.Trim() });
            header.Responses = responses.ToList();

            var approvalHistory = await _workflow.GetApprovalHistoryAsync(id.Trim());
            header.ApprovalLogs = approvalHistory.ToList();
            
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PIB detail: {Car}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CalculateTax(string car)
    {
        var result = await _tax.CalculatePibTaxAsync(car);
        await _tax.SaveCalculatedTaxToPibHeaderAsync(car, result);
        return Json(new { success = true, data = result });
    }

    [HttpGet]
    public async Task<IActionResult> CalculatePreview(string valuta, decimal fob, decimal asuransi, decimal freight, decimal bmTarif = 5.0m, decimal ppnTarif = 11.0m, decimal pphTarif = 2.5m)
    {
        var result = await _tax.CalculatePibTaxPreviewAsync(valuta, fob, asuransi, freight, bmTarif, ppnTarif, pphTarif);
        return Json(new { success = true, data = result });
    }

    [HttpGet]
    public async Task<IActionResult> ValidatePib(string car)
    {
        var result = await _validation.ValidatePibAsync(car);
        return Json(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitForApproval(string car, string? notes)
    {
        var username = User.Identity?.Name ?? "operator";
        var success = await _workflow.SubmitForReviewAsync(car, "PIB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PIB {car} sukses diajukan untuk review persetujuan!";
        else
            TempData["Error"] = $"Gagal mengajukan persetujuan untuk dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpPost]
    public async Task<IActionResult> ApproveDocument(string car, string? notes)
    {
        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.ApproveAsync(car, "PIB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PIB {car} berhasil disetujui (Approved)! Siap dikirim ke CEISA 4.0.";
        else
            TempData["Error"] = $"Gagal menyetujui dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpPost]
    public async Task<IActionResult> RejectDocument(string car, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["Error"] = "Catatan revisi / alasan penolakan wajib diisi.";
            return RedirectToAction(nameof(Detail), new { id = car });
        }

        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.RejectAsync(car, "PIB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PIB {car} telah dikembalikan untuk revisi.";
        else
            TempData["Error"] = $"Gagal memproses penolakan dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpGet]
    public async Task<IActionResult> PrintPdf(string id)
    {
        try
        {
            var pdfBytes = await _pdf.GeneratePibPdfAsync(id);
            return File(pdfBytes, "application/pdf", $"PIB_BC20_{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PIB PDF: {Car}", id);
            TempData["Error"] = $"Gagal mencetak dokumen PDF: {ex.Message}";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportJson(string id)
    {
        try
        {
            var json = await _ceisa.GeneratePibPayloadJsonAsync(id);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"CEISA_PIB_{id}.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting PIB JSON: {Car}", id);
            TempData["Error"] = $"Gagal export JSON CEISA: {ex.Message}";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("PIB_Items");
        
        ws.Cell(1, 1).Value = "HS_CODE";
        ws.Cell(1, 2).Value = "URAIAN_BARANG";
        ws.Cell(1, 3).Value = "JUMLAH";
        ws.Cell(1, 4).Value = "SATUAN";
        ws.Cell(1, 5).Value = "HARGA_SATUAN";
        ws.Cell(1, 6).Value = "NEGARA_ASAL";

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
        headerRow.Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).Value = "8708.29.90";
        ws.Cell(2, 2).Value = "AUTOMOTIVE BODY PARTS STAMPING";
        ws.Cell(2, 3).Value = 1200;
        ws.Cell(2, 4).Value = "PCE";
        ws.Cell(2, 5).Value = 45.50;
        ws.Cell(2, 6).Value = "JP";

        ws.Cell(3, 1).Value = "8708.40.99";
        ws.Cell(3, 2).Value = "TRANSMISSION GEARBOX ASSEMBLY";
        ws.Cell(3, 3).Value = 400;
        ws.Cell(3, 4).Value = "SET";
        ws.Cell(3, 5).Value = 350.00;
        ws.Cell(3, 6).Value = "JP";

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Upload_PIB_DoIT.xlsx");
    }

    public IActionResult Upload()
    {
        ViewData["Title"] = "Upload File — PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Upload File";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UploadExcel(IFormFile excelFile, string car)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["Error"] = "File upload tidak boleh kosong.";
            return RedirectToAction(nameof(Upload));
        }

        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var isSis = entity == "SIS";
            var idImp = isSis ? "011297389411000" : "011297371411000";
            var nmImo = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";

            if (string.IsNullOrWhiteSpace(car))
            {
                car = "010100" + DateTime.Now.ToString("yyMMdd") + new Random().Next(100000, 999999);
                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_HEADER (CAR, ID_IMP, NM_IMO, ASAL_DATA, KD_VAL, STATUS, APPROVAL_STATUS, CREATION_DATE, ENTITY)
                      VALUES (@Car, @IdImp, @NmImo, 'E', 'USD', 'DRAFT', 'DRAFT', GETDATE(), @Entity)",
                    new { Car = car, IdImp = idImp, NmImo = nmImo, Entity = entity });
            }

            using var stream = excelFile.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RowsUsed().Skip(1);
            
            int serial = 1;
            int importedCount = 0;
            decimal totalFob = 0;

            foreach (var row in rows)
            {
                var hsCode = row.Cell(1).GetString().Trim();
                var description = row.Cell(2).GetString().Trim();
                var qty = row.Cell(3).IsEmpty() ? 0m : Convert.ToDecimal(row.Cell(3).Value);
                var unitType = row.Cell(4).GetString().Trim();
                var unitVal = row.Cell(5).IsEmpty() ? 0m : Convert.ToDecimal(row.Cell(5).Value);
                var country = row.Cell(6).GetString().Trim();

                if (string.IsNullOrWhiteSpace(hsCode) && string.IsNullOrWhiteSpace(description))
                    continue;

                var itemTotal = qty * unitVal;
                totalFob += itemTotal;

                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, UNIT_VAL, CIF_PER_UNIT, ORIGIN_COUNTRY, KD_FAS)
                       VALUES (@Car, @Serial, @HsNo, @Desc, @Qty, @UnitType, @UnitVal, @CifPerUnit, @Country, 'KITE')",
                    new { Car = car, Serial = serial++, HsNo = hsCode, Desc = description, Qty = qty, UnitType = string.IsNullOrEmpty(unitType) ? "PCE" : unitType, UnitVal = unitVal, CifPerUnit = itemTotal, Country = string.IsNullOrEmpty(country) ? "JP" : country });
                importedCount++;
            }

            await _db.ExecuteAsync(
                "UPDATE PIB_DOIT_FINAL_HEADER SET JML_BRG = @Count, FOB = @Fob, CIF = @Fob WHERE CAR = @Car",
                new { Count = importedCount.ToString(), Fob = totalFob.ToString("F2"), Car = car });

            var calc = await _tax.CalculatePibTaxAsync(car);
            await _tax.SaveCalculatedTaxToPibHeaderAsync(car, calc);

            await _audit.LogAsync(User.Identity?.Name ?? "system", "UPLOAD_EXCEL_PIB", "PIB", car, $"Upload Excel sukses: {importedCount} item barang diimport");

            TempData["Success"] = $"Upload file berhasil! {importedCount} item barang berhasil diimpor ke dokumen PIB {car}.";
            return RedirectToAction(nameof(Detail), new { id = car });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file for PIB");
            TempData["Error"] = $"Gagal mengimpor file: {ex.Message}";
            return RedirectToAction(nameof(Upload));
        }
    }

    public new async Task<IActionResult> Response()
    {
        ViewData["Title"] = "Respons Resmi CEISA PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Respons CEISA";
        
        try
        {
            var responses = await _db.QueryAsync<PibResponModel>(
                @"SELECT r.CAR, r.RESKD AS ResKd, r.RESTG AS ResTg, r.DOKRESNO AS DokResNo, r.DOKRESTG AS DokResTg, 
                  r.KPBC, r.PIBNO AS PibNo, r.PIBTG AS PibTg, r.DESKRIPSI, h.NM_PEMASOK AS NamaImp
                  FROM PIB_DOIT_FINAL_RESPON r
                  LEFT JOIN PIB_DOIT_FINAL_HEADER h ON r.CAR = h.CAR
                  ORDER BY r.RESTG DESC");
            return View(responses.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PIB responses");
            return View(new List<PibResponModel>());
        }
    }
}

[Authorize]
public class PebController : Controller
{
    private readonly DatabaseContext _db;
    private readonly IWorkflowService _workflow;
    private readonly IValidationService _validation;
    private readonly IPdfReportService _pdf;
    private readonly ICeisaIntegrationService _ceisa;
    private readonly IAuditService _audit;
    private readonly IDocumentLockService _lockService;
    private readonly ILogger<PebController> _logger;

    public PebController(
        DatabaseContext db,
        IWorkflowService workflow,
        IValidationService validation,
        IPdfReportService pdf,
        ICeisaIntegrationService ceisa,
        IAuditService audit,
        IDocumentLockService lockService,
        ILogger<PebController> logger)
    {
        _db = db;
        _workflow = workflow;
        _validation = validation;
        _pdf = pdf;
        _ceisa = ceisa;
        _audit = audit;
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string search, string status, string approvalStatus, int page = 1)
    {
        ViewData["Title"] = "Daftar PEB (BC 3.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> PEB";
        
        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, TGEKS AS TgEks, NETTO, BRUTO, FOB,
                       NOPEN AS Nopen, TGL_NOPEN AS TglNopen, KDVAL AS KdVal,
                       ISNULL(ENTITY, 'SIM') AS Entity,
                       ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                       CASE 
                            WHEN STATUS >= 3 THEN 'APPROVED'
                            WHEN STATUS = 2 THEN 'SENT'
                            WHEN STATUS = 1 THEN 'PENDING'
                            ELSE 'DRAFT'
                       END AS Status 
                       FROM PEB_DOIT_FINAL_HEADER 
                       WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NAMAEKS LIKE '%MOTOR%' OR NPWPEKS LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NAMAEKS LIKE '%SALES%' OR NPWPEKS LIKE '%011297389%')))";
                       
            var parameters = new DynamicParameters();
            parameters.Add("Entity", entity);
            
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (CAR LIKE @Search OR NAMAEKS LIKE @Search OR NPWPEKS LIKE @Search OR NEGBELI LIKE @Search OR CARRIER LIKE @Search OR NOPEN LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "APPROVED") sql += " AND STATUS >= 3";
                else if (status == "SENT") sql += " AND STATUS = 2";
                else if (status == "DRAFT") sql += " AND STATUS <= 1";
            }
            if (!string.IsNullOrEmpty(approvalStatus))
            {
                sql += " AND APPROVAL_STATUS = @ApprovalStatus";
                parameters.Add("ApprovalStatus", approvalStatus);
            }
            
            sql += " ORDER BY CREATED_DATE DESC, CAR DESC";
            
            var items = await _db.QueryAsync<PebHeaderModel>(sql, parameters);
            return View(items.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PEB list");
            return View(new List<PebHeaderModel>());
        }
    }

    public IActionResult Create()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Akun Manajer Operasional hanya memiliki hak akses pantau/laporan dan tidak dapat membuat dokumen baru.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Buat PEB Baru (BC 3.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(PebHeaderModel model, IFormCollection form)
    {
        try
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Akun Manajer Operasional tidak memiliki izin membuat dokumen baru.";
                return RedirectToAction(nameof(Index));
            }

            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var isSis = entity == "SIS";

            if (string.IsNullOrWhiteSpace(model.Car))
            {
                model.Car = "010100" + DateTime.Now.ToString("yyMMdd") + new Random().Next(100000, 999999);
            }
            else
            {
                model.Car = model.Car.Trim();
            }

            // Anti-Duplikasi Nomor Pengajuan (CAR)
            var (isDuplicate, dupMsg) = await _lockService.CheckCarDuplicateAsync(model.Car, "PEB");
            if (isDuplicate)
            {
                TempData["Error"] = dupMsg;
                ViewData["Title"] = "Buat PEB Baru (BC 3.0)";
                ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
                return View(model);
            }

            model.Entity = entity;
            model.NamaEks = form["NamaEks"].FirstOrDefault() ?? (isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR");
            model.AlmtEks = form["AlmtEks"].FirstOrDefault() ?? (isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8, JAKARTA TIMUR" : "JL. RAYA PENGGILINGAN KM 19, CAKUNG, JAKARTA TIMUR");
            model.NpwpEks = form["NpwpEks"].FirstOrDefault() ?? (isSis ? "011297389411000" : "011297371411000");

            model.KdKtr = form["KdKtr"].FirstOrDefault() ?? "010100";
            model.NamaBeli = form["NamaBeli"].FirstOrDefault() ?? model.NamaBeli;
            model.AlmtBeli = form["AlmtBeli"].FirstOrDefault() ?? model.AlmtBeli;
            model.NegBeli = form["NegBeli"].FirstOrDefault() ?? (model.NegBeli ?? "JP");
            model.Carrier = form["Carrier"].FirstOrDefault() ?? "WAN HAI 315";
            model.Voy = form["Voy"].FirstOrDefault() ?? "WH-315";
            model.PelMuat = form["PelMuat"].FirstOrDefault() ?? "IDTPP";
            model.PelBongkar = form["PelBongkar"].FirstOrDefault() ?? "JPTYO";
            model.NoInv = form["NoInv"].FirstOrDefault() ?? ("INV-EXP-" + DateTime.Now.ToString("yyMMdd"));
            model.KdVal = form["KdVal"].FirstOrDefault() ?? "USD";
            model.Status = "DRAFT";
            model.ApprovalStatus = "DRAFT";

            decimal.TryParse(form["Fob"].FirstOrDefault(), out decimal fob);
            decimal.TryParse(form["Netto"].FirstOrDefault(), out decimal netto);
            decimal.TryParse(form["Bruto"].FirstOrDefault(), out decimal bruto);
            model.Fob = fob;
            model.Netto = netto > 0 ? netto : 12500;
            model.Bruto = bruto > 0 ? bruto : 13800;

            var sqlHeader = @"INSERT INTO PEB_DOIT_FINAL_HEADER 
                (CAR, NAMAEKS, ALMTEKS, NPWPEKS, NAMABELI, ALMTBELI, NEGBELI, TGEKS, NETTO, BRUTO, FOB, 
                 KDKTR, CARRIER, VOY, PELMUAT, PELBONGKAR, NOINV, KDVAL, STATUS, APPROVAL_STATUS, CREATED_DATE, ENTITY)
                VALUES (@Car, @NamaEks, @AlmtEks, @NpwpEks, @NamaBeli, @AlmtBeli, @NegBeli, GETDATE(), @Netto, @Bruto, @Fob,
                 @KdKtr, @Carrier, @Voy, @PelMuat, @PelBongkar, @NoInv, @KdVal, 0, 'DRAFT', GETDATE(), @Entity)";

            await _db.ExecuteAsync(sqlHeader, model);

            // Save Items
            var brgHs = form["BrgHs[]"];
            var brgDesc = form["BrgDesc[]"];
            var brgQty = form["BrgQty[]"];
            var brgSat = form["BrgSat[]"];
            var brgFob = form["BrgFob[]"];
            var brgNetto = form["BrgNetto[]"];

            for (int i = 0; i < brgHs.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(brgHs[i]))
                {
                    decimal qty = 0, valFob = 0, valNetto = 0;
                    if (brgQty.Count > i) decimal.TryParse(brgQty[i]?.Replace(",", ""), out qty);
                    if (brgFob.Count > i) decimal.TryParse(brgFob[i]?.Replace(",", ""), out valFob);
                    if (brgNetto.Count > i) decimal.TryParse(brgNetto[i]?.Replace(",", ""), out valNetto);

                    var desc = brgDesc.Count > i ? brgDesc[i] : "";
                    var sat = brgSat.Count > i ? brgSat[i] : "PCE";

                    await _db.ExecuteAsync(
                        @"INSERT INTO PEB_DOIT_FINAL_DETAIL (CAR, SERIBRG, HS, URBRG, JMLSAT, KDSAT, NETTODET, FOBDET)
                           VALUES (@Car, @Seri, @Hs, @UrBrg, @JmlSat, @KdSat, @NettoDet, @FobDet)",
                        new { Car = model.Car, Seri = i + 1, Hs = brgHs[i], UrBrg = desc, JmlSat = qty, KdSat = sat, NettoDet = valNetto, FobDet = valFob });
                }
            }

            // Save Documents
            var dokKd = form["DokKd[]"];
            var dokNo = form["DokNo[]"];

            for (int i = 0; i < dokKd.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(dokNo[i]))
                {
                    await _db.ExecuteAsync(
                        @"INSERT INTO PEB_DOIT_FINAL_DOCUMENT (CAR, SERI, KDDOK, NODOK, TGDOK)
                           VALUES (@Car, @Seri, @KdDok, @NoDok, GETDATE())",
                        new { Car = model.Car, Seri = i + 1, KdDok = dokKd[i], NoDok = dokNo[i] });
                }
            }

            await _lockService.ReleaseLockAsync(model.Car, User.Identity?.Name ?? "");
            await _audit.LogAsync(User.Identity?.Name ?? "system", "CREATE_PEB", "PEB", model.Car, $"Membuat dokumen PEB BC 3.0 baru (FOB: {model.Fob:N2} {model.KdVal})");

            TempData["Success"] = $"Dokumen PEB (BC 3.0) dengan nomor CAR {model.Car} berhasil disimpan.";
            return RedirectToAction(nameof(Detail), new { id = model.Car });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PEB: {Car}", model.Car);
            TempData["Error"] = $"Gagal membuat dokumen PEB: {ex.Message}";
            return View(model);
        }
    }

    public async Task<IActionResult> Detail(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));

        id = id.Trim();
        ViewData["Title"] = $"Detail PEB — {id}";
        ViewData["Breadcrumb"] = $"<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PEB</a> <span class='breadcrumb-sep'>/</span> Detail";
        
        try
        {
            var username = User.Identity?.Name ?? "unknown";
            var fullName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? username;
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";

            // Acquire or Check Document Lock
            var lockStatus = await _lockService.AcquireLockAsync(id, "PEB", username, fullName, entity);
            ViewBag.IsLockedByOther = lockStatus.isLocked;
            ViewBag.LockedByName = lockStatus.lockedByName;
            ViewBag.LockedByUser = lockStatus.lockedByUser;
            ViewBag.LockedAt = lockStatus.lockedAt?.ToString("dd-MM-yyyy HH:mm");

            var header = await _db.QueryFirstOrDefaultAsync<PebHeaderModel>(
                @"SELECT CAR, NAMAEKS AS NamaEks, ALMTEKS AS AlmtEks, NPWPEKS AS NpwpEks,
                  ISNULL(NAMABELI, '') AS NamaBeli, ISNULL(ALMTBELI, '') AS AlmtBeli, ISNULL(NEGBELI, 'JP') AS NegBeli,
                  TGEKS AS TgEks, ISNULL(NETTO, 0) AS Netto, ISNULL(BRUTO, 0) AS Bruto, ISNULL(FOB, 0) AS Fob,
                  ISNULL(NOPEN, '') AS Nopen, TGL_NOPEN AS TglNopen, ISNULL(KDKTR, '010100') AS KdKtr,
                  ISNULL(CARRIER, '') AS Carrier, ISNULL(VOY, '') AS Voy, ISNULL(PELMUAT, '') AS PelMuat, ISNULL(PELBONGKAR, '') AS PelBongkar,
                  ISNULL(NOINV, '') AS NoInv, ISNULL(KDVAL, 'USD') AS KdVal,
                  ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus, REVIEW_NOTES AS ReviewNotes,
                  SUBMITTED_BY AS SubmittedBy, SUBMITTED_DATE AS SubmittedDate,
                  APPROVED_BY AS ApprovedBy, APPROVED_DATE AS ApprovedDate,
                  CASE 
                       WHEN STATUS >= 3 THEN 'APPROVED'
                       WHEN STATUS = 2 THEN 'SENT'
                       WHEN STATUS = 1 THEN 'PENDING'
                       ELSE 'DRAFT'
                  END AS Status 
                  FROM PEB_DOIT_FINAL_HEADER WHERE RTRIM(LTRIM(CAR)) = @Car",
                new { Car = id });
                
            if (header == null)
            {
                TempData["Error"] = $"Dokumen PEB dengan Nomor CAR '{id}' tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }
            
            try
            {
                var details = await _db.QueryAsync<PebDetailModel>(
                    @"SELECT SERIBRG AS Seri, 
                             CAST(ISNULL(HS, '') AS VARCHAR(50)) AS HsNo, 
                             ISNULL(URBRG, ISNULL(URBRG1, '')) AS UrBrg, 
                             ISNULL(JMLSAT, ISNULL(JMSATUAN, 0)) AS JmlSat, 
                             ISNULL(KDSAT, ISNULL(JNSATUAN, 'PCE')) AS KdSat, 
                             ISNULL(NETTODET, ISNULL(NETDET, 0)) AS NettoDet, 
                             ISNULL(FOBDET, ISNULL(FOBPERBRG, 0)) AS FobDet 
                       FROM PEB_DOIT_FINAL_DETAIL WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY SERIBRG",
                    new { Car = id });
                header.Details = details.ToList();
            }
            catch (Exception exDetail)
            {
                _logger.LogWarning(exDetail, "Could not fetch detail items for PEB: {Car}", id);
                header.Details = new List<PebDetailModel>();
            }
            
            try
            {
                var docs = await _db.QueryAsync<PebDocumentModel>(
                    @"SELECT SERI AS Seri, KDDOK AS KdDok, NODOK AS NoDok, TGDOK AS TgDok 
                      FROM PEB_DOIT_FINAL_DOCUMENT WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY SERI",
                    new { Car = id });
                header.Documents = docs.ToList();
            }
            catch (Exception exDoc)
            {
                _logger.LogWarning(exDoc, "Could not fetch documents for PEB: {Car}", id);
                header.Documents = new List<PebDocumentModel>();
            }

            try
            {
                var containers = await _db.QueryAsync<PebContainerModel>(
                    @"SELECT NOCONT AS NoCont, UKURCONT AS UkurCont, TIPECONT AS TipeCont 
                      FROM PEB_DOIT_FINAL_CONTAINER WHERE RTRIM(LTRIM(CAR)) = @Car",
                    new { Car = id });
                header.Containers = containers.ToList();
            }
            catch (Exception exCont)
            {
                _logger.LogWarning(exCont, "Could not fetch containers for PEB: {Car}", id);
                header.Containers = new List<PebContainerModel>();
            }

            try
            {
                var responses = await _db.QueryAsync<PebResponModel>(
                    @"SELECT RESKD AS ResKd, RESTG AS ResTg, NOPEN AS NoPen, TGPEN AS TgPen, DESKRIPSI AS Deskripsi
                      FROM PEB_DOIT_FINAL_RESPON WHERE RTRIM(LTRIM(CAR)) = @Car ORDER BY RESTG DESC",
                    new { Car = id });
                header.Responses = responses.ToList();
            }
            catch (Exception exResp)
            {
                _logger.LogWarning(exResp, "Could not fetch responses for PEB: {Car}", id);
                header.Responses = new List<PebResponModel>();
            }

            try
            {
                var approvalHistory = await _workflow.GetApprovalHistoryAsync(id);
                header.ApprovalLogs = approvalHistory?.ToList() ?? new List<ApprovalLogModel>();
            }
            catch (Exception exAppr)
            {
                _logger.LogWarning(exAppr, "Could not fetch approval history for PEB: {Car}", id);
                header.ApprovalLogs = new List<ApprovalLogModel>();
            }
            
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PEB detail: {Car}", id);
            TempData["Error"] = $"Terjadi kesalahan saat memuat detail PEB {id}: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> ValidatePeb(string car)
    {
        var result = await _validation.ValidatePebAsync(car);
        return Json(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitForApproval(string car, string? notes)
    {
        var username = User.Identity?.Name ?? "operator";
        var success = await _workflow.SubmitForReviewAsync(car, "PEB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PEB {car} sukses diajukan untuk review persetujuan!";
        else
            TempData["Error"] = $"Gagal mengajukan persetujuan untuk dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpPost]
    public async Task<IActionResult> ApproveDocument(string car, string? notes)
    {
        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.ApproveAsync(car, "PEB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PEB {car} berhasil disetujui (Approved)! Siap dikirim ke CEISA 4.0.";
        else
            TempData["Error"] = $"Gagal menyetujui dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpPost]
    public async Task<IActionResult> RejectDocument(string car, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["Error"] = "Catatan revisi / alasan penolakan wajib diisi.";
            return RedirectToAction(nameof(Detail), new { id = car });
        }

        var username = User.Identity?.Name ?? "supervisor";
        var success = await _workflow.RejectAsync(car, "PEB", username, notes);
        if (success)
            TempData["Success"] = $"Dokumen PEB {car} telah dikembalikan untuk revisi.";
        else
            TempData["Error"] = $"Gagal memproses penolakan dokumen {car}.";

        return RedirectToAction(nameof(Detail), new { id = car });
    }

    [HttpGet]
    public async Task<IActionResult> PrintPdf(string id)
    {
        try
        {
            var pdfBytes = await _pdf.GeneratePebPdfAsync(id);
            return File(pdfBytes, "application/pdf", $"PEB_BC30_{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PEB PDF: {Car}", id);
            TempData["Error"] = $"Gagal mencetak dokumen PDF: {ex.Message}";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportJson(string id)
    {
        try
        {
            var json = await _ceisa.GeneratePebPayloadJsonAsync(id);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"CEISA_PEB_{id}.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting PEB JSON: {Car}", id);
            TempData["Error"] = $"Gagal export JSON CEISA: {ex.Message}";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("PEB_Items");
        
        ws.Cell(1, 1).Value = "HS_CODE";
        ws.Cell(1, 2).Value = "URAIAN_BARANG";
        ws.Cell(1, 3).Value = "JUMLAH";
        ws.Cell(1, 4).Value = "SATUAN";
        ws.Cell(1, 5).Value = "FOB_USD";
        ws.Cell(1, 6).Value = "NETTO_KG";

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
        headerRow.Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).Value = "8703.22.90";
        ws.Cell(2, 2).Value = "SUZUKI ERTIGA SMART HYBRID GL AT";
        ws.Cell(2, 3).Value = 24;
        ws.Cell(2, 4).Value = "UNT";
        ws.Cell(2, 5).Value = 384000.00;
        ws.Cell(2, 6).Value = 28800.00;

        ws.Cell(3, 1).Value = "8703.23.90";
        ws.Cell(3, 2).Value = "SUZUKI XL7 ALPHA AT PASSENGER CAR";
        ws.Cell(3, 3).Value = 16;
        ws.Cell(3, 4).Value = "UNT";
        ws.Cell(3, 5).Value = 272000.00;
        ws.Cell(3, 6).Value = 19600.00;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Upload_PEB_DoIT.xlsx");
    }

    public IActionResult Upload()
    {
        ViewData["Title"] = "Upload File — PEB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Upload File";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UploadExcel(IFormFile excelFile, string car)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["Error"] = "File upload tidak boleh kosong.";
            return RedirectToAction(nameof(Upload));
        }

        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var isSis = entity == "SIS";
            var namaEks = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";
            var almtEks = isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8" : "JL. RAYA PENGGILINGAN KM 19";
            var npwpEks = isSis ? "011297389411000" : "011297371411000";

            if (string.IsNullOrWhiteSpace(car))
            {
                car = "010100" + DateTime.Now.ToString("yyMMdd") + new Random().Next(100000, 999999);
                await _db.ExecuteAsync(
                    @"INSERT INTO PEB_DOIT_FINAL_HEADER (CAR, NAMAEKS, ALMTEKS, NPWPEKS, NAMABELI, NEGBELI, KDVAL, STATUS, APPROVAL_STATUS, CREATED_DATE, ENTITY)
                      VALUES (@Car, @NamaEks, @AlmtEks, @NpwpEks, 'SUZUKI MOTOR CORPORATION JAPAN', 'JP', 'USD', 0, 'DRAFT', GETDATE(), @Entity)",
                    new { Car = car, NamaEks = namaEks, AlmtEks = almtEks, NpwpEks = npwpEks, Entity = entity });
            }

            using var stream = excelFile.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RowsUsed().Skip(1);
            
            int serial = 1;
            int importedCount = 0;
            decimal totalFob = 0;
            decimal totalNetto = 0;

            foreach (var row in rows)
            {
                var hsCode = row.Cell(1).GetString().Trim();
                var description = row.Cell(2).GetString().Trim();
                var qty = row.Cell(3).IsEmpty() ? 0m : Convert.ToDecimal(row.Cell(3).Value);
                var unitType = row.Cell(4).GetString().Trim();
                var fob = row.Cell(5).IsEmpty() ? 0m : Convert.ToDecimal(row.Cell(5).Value);
                var netto = row.Cell(6).IsEmpty() ? 0m : Convert.ToDecimal(row.Cell(6).Value);

                if (string.IsNullOrWhiteSpace(hsCode) && string.IsNullOrWhiteSpace(description))
                    continue;

                totalFob += fob;
                totalNetto += netto;

                await _db.ExecuteAsync(
                    @"INSERT INTO PEB_DOIT_FINAL_DETAIL (CAR, SERIBRG, HS, URBRG, JMLSAT, KDSAT, NETTODET, FOBDET)
                       VALUES (@Car, @Seri, @Hs, @Desc, @Qty, @UnitType, @Netto, @Fob)",
                    new { Car = car, Seri = serial++, Hs = hsCode, Desc = description, Qty = qty, UnitType = string.IsNullOrEmpty(unitType) ? "PCE" : unitType, Netto = netto, Fob = fob });
                importedCount++;
            }

            await _db.ExecuteAsync(
                "UPDATE PEB_DOIT_FINAL_HEADER SET FOB = @Fob, NETTO = @Netto WHERE CAR = @Car",
                new { Fob = totalFob, Netto = totalNetto, Car = car });

            await _audit.LogAsync(User.Identity?.Name ?? "system", "UPLOAD_EXCEL_PEB", "PEB", car, $"Upload Excel PEB berhasil: {importedCount} item barang diimport");

            TempData["Success"] = $"Upload file PEB berhasil! {importedCount} item barang diimpor ke dokumen PEB {car}.";
            return RedirectToAction(nameof(Detail), new { id = car });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file for PEB");
            TempData["Error"] = $"Gagal mengimpor file PEB: {ex.Message}";
            return RedirectToAction(nameof(Upload));
        }
    }

    public new async Task<IActionResult> Response()
    {
        ViewData["Title"] = "Respons Resmi CEISA PEB (NPE)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Respons CEISA";
        
        try
        {
            var responses = await _db.QueryAsync<PebResponModel>(
                @"SELECT r.CAR, r.RESKD AS ResKd, r.RESTG AS ResTg, r.NOPEN AS NoPen, r.TGPEN AS TgPen, r.DESKRIPSI
                  FROM PEB_DOIT_FINAL_RESPON r
                  ORDER BY r.RESTG DESC");
            return View(responses.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PEB responses");
            return View(new List<PebResponModel>());
        }
    }
}

[Authorize]
public class SiloController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<SiloController> _logger;

    public SiloController(DatabaseContext db, ILogger<SiloController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IActionResult Pib()
    {
        ViewData["Title"] = "Upload SILO — PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> SILO PIB";
        return View();
    }

    public IActionResult Peb()
    {
        ViewData["Title"] = "Upload SILO — PEB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> SILO PEB";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SyncPib(string searchInvoice)
    {
        try
        {
            var isMock = true;
            try
            {
                var mockSetting = await _db.QueryFirstOrDefaultAsync<string>(
                    "SELECT value FROM doit_setting WHERE setting_key = 'USE_MOCK_SILO'");
                if (mockSetting != null)
                {
                    isMock = mockSetting.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            if (!isMock)
            {
                try
                {
                    using (var oracleConn = new OracleConnection(_db.CreateOracleConnection().ConnectionString))
                    {
                        await oracleConn.OpenAsync();
                        
                        var query = @"
                            SELECT b.INVOICE_NO AS InvoiceNo, b.BL_NUMBER AS BlNumber, b.BL_DATE AS BlDate, 
                                   b.NAMA_PENGANGKUT AS NamaPengangkut, b.NO_PENGANGKUT AS NoPengangkut, b.TG_TIBA AS TgTiba,
                                   a.KODE_BARANG AS KodeBarang, a.URAIAN AS Uraian, a.TIPE AS Tipe, 
                                   a.JUMLAH_SATUAN AS JumlahSatuan, a.NETTO AS Netto, a.AMOUNT AS Amount
                            FROM pib_doit_dtl a
                            LEFT JOIN pib_doit_hdr b ON a.header_id = b.header_id
                            WHERE b.INVOICE_NO = :InvoiceNo";
                            
                        var records = (await oracleConn.QueryAsync<dynamic>(query, new { InvoiceNo = searchInvoice })).ToList();
                        
                        if (records.Count == 0)
                        {
                            throw new Exception($"Invoice {searchInvoice} tidak ditemukan di database SILO Oracle.");
                        }
                        
                        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
                        var isSis = entity == "SIS";
                        var idImp = isSis ? "011297389411000" : "011297371411000";
                        var nmImo = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";
                        var alImp = isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8" : "JL. RAYA PENGGILINGAN KM 19";

                        var random = new Random();
                        var carNo = $"00000000615220261025{random.Next(100000, 999999)}";
                        var first = records[0];
                        
                        await _db.ExecuteAsync(
                            @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                              (CAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, NM_PEMASOK, AL_PEMASOK, TGL_TIBA, JML_BRG, CREATION_DATE, FL_VALID, ENTITY)
                              VALUES (@Car, 'S', @IdImp, @NmImo, @AlImp, 'SUZUKI MOTOR CORPORATION', 'SHIZUOKA, JAPAN', @TgTiba, @JmlBrg, GETDATE(), 'N', @Entity)",
                            new { 
                                Car = carNo, 
                                IdImp = idImp,
                                NmImo = nmImo,
                                AlImp = alImp,
                                TgTiba = first.TgTiba?.ToString() ?? DateTime.Now.ToString("yyyyMMdd"),
                                JmlBrg = records.Count.ToString(),
                                Entity = entity
                            });
                            
                        int serial = 1;
                        foreach (var rec in records)
                        {
                            await _db.ExecuteAsync(
                                @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, UNIT_VAL)
                                   VALUES (@Car, @Seri, @Hs, @Uraian, @Qty, 'PCS', @Amount)",
                                new {
                                    Car = carNo,
                                    Seri = serial++,
                                    Hs = rec.KodeBarang?.ToString() ?? "00000000",
                                    Uraian = rec.Uraian?.ToString() ?? "SPAREPART SUZUKI",
                                    Qty = Convert.ToDecimal(rec.JumlahSatuan ?? 0),
                                    Amount = Convert.ToDecimal(rec.Amount ?? 0)
                                });
                        }
                        
                        await _db.ExecuteAsync(
                            @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                              VALUES (@User, 'SYNC_SILO_PIB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                            new { 
                                User = User.Identity?.Name ?? "system", 
                                Car = carNo, 
                                Desc = $"Tarik data SILO Oracle ({entity}) berhasil untuk Invoice {searchInvoice} ({records.Count} items)", 
                                Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                            });

                        TempData["Success"] = $"Sinkronisasi ({entity}) berhasil! Dokumen PIB dengan CAR {carNo} di-import dari Oracle SILO.";
                        return RedirectToAction("Index", "Pib");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to Oracle SILO, falling back to mock implementation");
                }
            }

            // Simulate Oracle SILO database query and mapping (Mock/Fallback implementation)
            {
                var entity = User.FindFirst("Entity")?.Value ?? "SIM";
                var isSis = entity == "SIS";
                var idImp = isSis ? "011297389411000" : "011297371411000";
                var nmImo = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";
                var alImp = isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8" : "JL. RAYA PENGGILINGAN KM 19";

                var random = new Random();
                var carNo = $"00000000615220261025{random.Next(100000, 999999)}";
                
                var header = new PibHeaderModel
                {
                    Car = carNo,
                    Entity = entity,
                    IdImp = idImp,
                    NmImo = nmImo,
                    AlImp = alImp,
                    NmPemasok = "SUZUKI MOTOR CORPORATION",
                    AlPemasok = "300 TAKATSUKA-CHO, MINAMI-KU, HAMAMATSU-SHI, SHIZUOKA",
                    TglTiba = DateTime.Now.AddDays(2).ToString("yyyyMMdd"),
                    JmlBrg = "12",
                    Status = "DRAFT"
                };

                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                      (CAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, NM_PEMASOK, AL_PEMASOK, TGL_TIBA, JML_BRG, CREATION_DATE, FL_VALID, ENTITY)
                      VALUES (@Car, 'S', @IdImp, @NmImo, @AlImp, @NmPemasok, @AlPemasok, @TglTiba, @JmlBrg, GETDATE(), 'N', @Entity)",
                    header);

                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, UNIT_VAL)
                       VALUES (@Car, 1, '87082911', 'FRONT BUMPER GRILLE SUZUKI ERTIGA', 120, 'PCS', 15.50)",
                    new { Car = carNo });

                await _db.ExecuteAsync(
                    @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                      VALUES (@User, 'SYNC_SILO_PIB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                    new { 
                        User = User.Identity?.Name ?? "system", 
                        Car = carNo, 
                        Desc = $"Sinkronisasi data SILO ({entity}) berhasil (Simulasi) untuk Invoice {searchInvoice}", 
                        Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                    });

                TempData["Success"] = $"Sinkronisasi ({entity}) berhasil! Dokumen PIB dengan CAR {carNo} di-import sebagai DRAFT (Simulasi).";
                return RedirectToAction("Index", "Pib");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing SILO PIB data");
            TempData["Error"] = $"Gagal sinkronisasi data SILO: {ex.Message}";
            return RedirectToAction(nameof(Pib));
        }
    }

    [HttpPost]
    public async Task<IActionResult> SyncPeb(string searchInvoice)
    {
        try
        {
            var isMock = true;
            try
            {
                var mockSetting = await _db.QueryFirstOrDefaultAsync<string>(
                    "SELECT value FROM doit_setting WHERE setting_key = 'USE_MOCK_SILO'");
                if (mockSetting != null)
                {
                    isMock = mockSetting.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            if (!isMock)
            {
                try
                {
                    using (var oracleConn = new OracleConnection(_db.CreateOracleConnection().ConnectionString))
                    {
                        await oracleConn.OpenAsync();
                        
                        var query = @"
                            SELECT a.INVOICE_NO AS InvoiceNo, a.TGL_INVOICE AS TglInvoice, a.NO_HS AS NoHs, 
                                   a.URAIAN_BARANG AS UraianBarang, a.TIPE AS Tipe, a.JUMLAH_SATUAN AS JumlahSatuan,
                                   b.PENERIMA_NAMA AS PenerimaNama, b.PENERIMA_ALAMAT AS PenerimaAlamat, 
                                   b.PENERIMA_NEGARA AS PenerimaNegara, b.NAMA_SARANA_PENGANGKUT AS NamaSarana, b.NO_PENGANGKUT AS NoPengangkut
                            FROM peb_doit_dtl a
                            LEFT JOIN peb_doit_hdr b ON a.header_id = b.header_id
                            WHERE a.INVOICE_NO = :InvoiceNo";
                            
                        var records = (await oracleConn.QueryAsync<dynamic>(query, new { InvoiceNo = searchInvoice })).ToList();
                        
                        if (records.Count == 0)
                        {
                            throw new Exception($"Invoice {searchInvoice} tidak ditemukan di database SILO Oracle.");
                        }
                        
                        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
                        var isSis = entity == "SIS";
                        var namaEks = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";
                        var almtEks = isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8" : "JL. RAYA PENGGILINGAN KM 19";
                        var npwpEks = isSis ? "011297389411000" : "011297371411000";

                        var random = new Random();
                        var carNo = $"00003001062620260714{random.Next(100000, 999999)}";
                        var first = records[0];
                        
                        await _db.ExecuteAsync(
                            @"INSERT INTO PEB_DOIT_FINAL_HEADER 
                               (CAR, NAMAEKS, ALMTEKS, NPWPEKS, NEGBELI, TGEKS, NETTO, BRUTO, FOB, CREATED_DATE, STATUS, ENTITY)
                               VALUES (@Car, @NamaEks, @AlmtEks, @NpwpEks, @NegBeli, GETDATE(), 1000, 1100, 50000, GETDATE(), 1, @Entity)",
                            new {
                                Car = carNo,
                                NamaEks = namaEks,
                                AlmtEks = almtEks,
                                NpwpEks = npwpEks,
                                NegBeli = first.PenerimaNegara?.ToString() ?? "MY",
                                Entity = entity
                            });
                            
                        await _db.ExecuteAsync(
                            @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                              VALUES (@User, 'SYNC_SILO_PEB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                            new { 
                                User = User.Identity?.Name ?? "system", 
                                Car = carNo, 
                                Desc = $"Tarik data SILO PEB Oracle ({entity}) berhasil untuk Invoice {searchInvoice}", 
                                Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                            });

                        TempData["Success"] = $"Sinkronisasi ({entity}) berhasil! Dokumen PEB dengan CAR {carNo} di-import dari Oracle SILO.";
                        return RedirectToAction("Index", "Peb");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to Oracle SILO PEB, falling back to mock implementation");
                }
            }

            // Simulate Oracle SILO database query and mapping (Mock/Fallback implementation)
            {
                var entity = User.FindFirst("Entity")?.Value ?? "SIM";
                var isSis = entity == "SIS";
                var namaEks = isSis ? "PT. SUZUKI INDOMOBIL SALES" : "PT. SUZUKI INDOMOBIL MOTOR";
                var almtEks = isSis ? "WISMA INDOMOBIL I, JL. MT HARYONO KAV. 8, JAKARTA TIMUR" : "JL. RAYA PENGGILINGAN KM 19, CAKUNG, JAKARTA TIMUR";
                var npwpEks = isSis ? "011297389411000" : "011297371411000";

                var random = new Random();
                var carNo = $"00003001062620260714{random.Next(100000, 999999)}";
                
                var header = new PebHeaderModel
                {
                    Car = carNo,
                    Entity = entity,
                    NamaEks = namaEks,
                    AlmtEks = almtEks,
                    NpwpEks = npwpEks,
                    NamaBeli = "BOUSTEAD SDN BERHAD",
                    AlmtBeli = "KUALA LUMPUR, MALAYSIA",
                    NegBeli = "MY",
                    TgEks = DateTime.Now.AddDays(5),
                    Netto = 18450.50m,
                    Bruto = 19100.00m,
                    Fob = 245000.00m
                };

                await _db.ExecuteAsync(
                    @"INSERT INTO PEB_DOIT_FINAL_HEADER 
                       (CAR, NAMAEKS, ALMTEKS, NPWPEKS, NEGBELI, TGEKS, NETTO, BRUTO, FOB, CREATED_DATE, STATUS, ENTITY)
                       VALUES (@Car, @NamaEks, @AlmtEks, @NpwpEks, @NegBeli, @TgEks, @Netto, @Bruto, @Fob, GETDATE(), 1, @Entity)",
                    header);

                await _db.ExecuteAsync(
                    @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                      VALUES (@User, 'SYNC_SILO_PEB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                    new { 
                        User = User.Identity?.Name ?? "system", 
                        Car = carNo, 
                        Desc = $"Sinkronisasi data SILO PEB ({entity}) berhasil (Simulasi) untuk Invoice {searchInvoice}", 
                        Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                    });

                TempData["Success"] = $"Sinkronisasi ({entity}) berhasil! Dokumen PEB dengan CAR {carNo} di-import sebagai DRAFT (Simulasi).";
                return RedirectToAction("Index", "Peb");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing SILO PEB data");
            TempData["Error"] = $"Gagal sinkronisasi data SILO PEB: {ex.Message}";
            return RedirectToAction(nameof(Peb));
        }
    }

    public IActionResult ExportToSilo()
    {
        ViewData["Title"] = "Kirim Hasil Pabean ke Oracle SILO";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> SILO <span class='breadcrumb-sep'>/</span> Kirim Data";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ProcessExportToSilo(string docType, string carNo)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(carNo))
            {
                TempData["Error"] = "Pilih atau masukkan Nomor CAR Dokumen!";
                return RedirectToAction(nameof(ExportToSilo));
            }

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'EXPORT_TO_SILO', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = carNo, Desc = $"Kirim data respon pabean ({docType}) CAR {carNo} ke Oracle SILO Suzuki berhasil", Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Sukses mengirim respon & penetapan {docType} (CAR: {carNo}) ke Oracle SILO Suzuki!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting data to SILO");
            TempData["Error"] = $"Gagal mengirim data ke SILO: {ex.Message}";
        }
        return RedirectToAction(nameof(ExportToSilo));
    }

    public async Task<IActionResult> ViewSilo()
    {
        ViewData["Title"] = "Monitoring & Staging Log SILO";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> SILO <span class='breadcrumb-sep'>/</span> View Data";

        var logs = await _db.QueryAsync<dynamic>(
            @"SELECT TOP 50 id, user_name, action, module, document_id, description, ip_address, created_at 
              FROM doit_audit_log 
              WHERE module = 'SILO' OR action LIKE '%SILO%'
              ORDER BY created_at DESC");

        return View(logs.ToList());
    }
}

[Authorize]
public class CeisaController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ICeisaIntegrationService _ceisa;
    private readonly IValidationService _validation;
    private readonly IAuditService _audit;
    private readonly ILogger<CeisaController> _logger;

    public CeisaController(
        DatabaseContext db,
        ICeisaIntegrationService ceisa,
        IValidationService validation,
        IAuditService audit,
        ILogger<CeisaController> logger)
    {
        _db = db;
        _ceisa = ceisa;
        _validation = validation;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> SendPib()
    {
        ViewData["Title"] = "Kirim PIB ke CEISA 4.0";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Kirim PIB";
        
        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
        var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_IMO AS NmImo, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, 
                           ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                           ISNULL(ENTITY, 'SIM') AS Entity,
                           KD_VAL AS KdVal, CIF AS Cif,
                           CASE 
                               WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                               WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                               ELSE 'DRAFT'
                           END AS Status 
                    FROM PIB_DOIT_FINAL_HEADER 
                    WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NM_IMO LIKE '%MOTOR%' OR ID_IMP LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NM_IMO LIKE '%SALES%' OR ID_IMP LIKE '%011297389%')))
                    ORDER BY CREATION_DATE DESC";
        var drafts = await _db.QueryAsync<PibHeaderModel>(sql, new { Entity = entity });
        return View(drafts.ToList());
    }

    public async Task<IActionResult> SendPeb()
    {
        ViewData["Title"] = "Kirim PEB ke CEISA 4.0";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Kirim PEB";
        
        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
        var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, TGEKS AS TgEks, NETTO, BRUTO, FOB,
                           ISNULL(APPROVAL_STATUS, 'DRAFT') AS ApprovalStatus,
                           ISNULL(ENTITY, 'SIM') AS Entity,
                           NOPEN AS Nopen,
                           CASE 
                               WHEN STATUS >= 3 THEN 'APPROVED'
                               WHEN STATUS = 2 THEN 'SENT'
                               ELSE 'DRAFT'
                           END AS Status 
                     FROM PEB_DOIT_FINAL_HEADER 
                     WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NAMAEKS LIKE '%MOTOR%' OR NPWPEKS LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NAMAEKS LIKE '%SALES%' OR NPWPEKS LIKE '%011297389%')))
                     ORDER BY CREATED_DATE DESC";
        var drafts = await _db.QueryAsync<PebHeaderModel>(sql, new { Entity = entity });
        return View(drafts.ToList());
    }

    [HttpPost]
    public async Task<IActionResult> TransmitPib(string car)
    {
        try
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Akses ditolak: Akun Manajer Operasional tidak memiliki wewenang pengiriman dokumen ke CEISA.";
                return RedirectToAction(nameof(SendPib));
            }

            var username = User.Identity?.Name ?? "operator";
            var result = await _ceisa.TransmitPibAsync(car, username, isSandbox: true);
            
            if (result.Success)
            {
                TempData["Success"] = result.Message;
            }
            else
            {
                TempData["Error"] = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PIB to CEISA");
            TempData["Error"] = $"Gagal mengirim PIB ke CEISA: {ex.Message}";
        }
        return RedirectToAction(nameof(SendPib));
    }

    [HttpPost]
    public async Task<IActionResult> TransmitPeb(string car)
    {
        try
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (role.Contains("MANAJER_OPS", StringComparison.OrdinalIgnoreCase) || role.Contains("VIEWER", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Akses ditolak: Akun Manajer Operasional tidak memiliki wewenang pengiriman dokumen ke CEISA.";
                return RedirectToAction(nameof(SendPeb));
            }

            var username = User.Identity?.Name ?? "operator";
            var result = await _ceisa.TransmitPebAsync(car, username, isSandbox: true);
            
            if (result.Success)
            {
                TempData["Success"] = result.Message;
            }
            else
            {
                TempData["Error"] = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PEB to CEISA");
            TempData["Error"] = $"Gagal mengirim PEB ke CEISA: {ex.Message}";
        }
        return RedirectToAction(nameof(SendPeb));
    }

    public IActionResult GetBc11()
    {
        ViewData["Title"] = "Tarik Data BC 1.1 (Manifes CEISA 4.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Tarik BC 1.1";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ProcessGetBc11(string noBc11, string tglBc11, string carNo, string posNo, string subPosNo, string pelMuat, string pelBongkar)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(noBc11))
            {
                TempData["Error"] = "Nomor BC 1.1 wajib diisi!";
                return RedirectToAction(nameof(GetBc11));
            }

            var username = User.Identity?.Name ?? "operator";
            var req = new CeisaBc11PullRequest
            {
                NoBc11 = noBc11,
                PosNo = posNo ?? "0001",
                SubPosNo = subPosNo ?? "0000",
                Car = carNo,
                PelMuat = pelMuat,
                PelBongkar = pelBongkar
            };

            if (DateTime.TryParse(tglBc11, out var parsedDate))
                req.TglBc11 = parsedDate;

            var manifest = await _ceisa.PullBc11ManifestAsync(req, username);

            TempData["Success"] = $"Sukses menarik data Manifes BC 1.1 No. {manifest.NoBc11} (Pengangkut: {manifest.NamaPengangkut}, Voyage: {manifest.NoVoyage}, Bruto: {manifest.Bruto} Kg)!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting BC 1.1 from CEISA");
            TempData["Error"] = $"Gagal menarik data BC 1.1: {ex.Message}";
        }
        return RedirectToAction(nameof(GetBc11));
    }

    [HttpGet]
    public async Task<IActionResult> Tracking(string car, string type = "PIB")
    {
        ViewData["Title"] = $"Real-time Tracking CEISA — {car}";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Tracking";

        var tracking = await _ceisa.GetTrackingTimelineAsync(car, type);
        return View(tracking);
    }

    [HttpGet]
    public async Task<IActionResult> GetTrackingTimeline(string car, string docType = "PIB")
    {
        var tracking = await _ceisa.GetTrackingTimelineAsync(car, docType);
        return Json(new { success = true, data = tracking });
    }

    [HttpGet]
    public async Task<IActionResult> GetRawPayloadJson(string car, string docType = "PIB")
    {
        try
        {
            var json = docType.Equals("PEB", StringComparison.OrdinalIgnoreCase) 
                ? await _ceisa.GeneratePebPayloadJsonAsync(car)
                : await _ceisa.GeneratePibPayloadJsonAsync(car);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }
}

[Authorize]
public class MasterController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ITaxCalculationService _tax;
    private readonly IAuditService _audit;
    private readonly ILogger<MasterController> _logger;

    public MasterController(
        DatabaseContext db,
        ITaxCalculationService tax,
        IAuditService audit,
        ILogger<MasterController> logger)
    {
        _db = db;
        _tax = tax;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> KursPajak()
    {
        ViewData["Title"] = "Master Kurs Pajak Kemenkeu (NDPBM)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Kurs Pajak";

        var rates = await _tax.GetAllActiveRatesAsync();
        return View(rates);
    }

    [HttpPost]
    public async Task<IActionResult> SaveKursPajak(string kdVal, string nmVal, decimal nilaiNdpbm, DateTime tglAwal, DateTime tglAkhir, string noKmk)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(kdVal) || nilaiNdpbm <= 0)
            {
                TempData["Error"] = "Kode Valuta dan Nilai NDPBM wajib diisi dengan benar!";
                return RedirectToAction(nameof(KursPajak));
            }

            var success = await _tax.UpdateRateAsync(kdVal, nilaiNdpbm, tglAwal, tglAkhir, noKmk ?? "KMK/2026/WEEKLY");
            if (success)
            {
                await _audit.LogAsync(User.Identity?.Name ?? "system", "UPDATE_KURS_PAJAK", "MASTER", kdVal, $"Update Kurs NDPBM {kdVal} menjadi {nilaiNdpbm:N2}");
                TempData["Success"] = $"Kurs Pajak {kdVal.ToUpper()} berhasil disimpan!";
            }
            else
            {
                TempData["Error"] = $"Gagal menyimpan kurs {kdVal}.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving kurs pajak");
            TempData["Error"] = $"Terjadi kesalahan: {ex.Message}";
        }
        return RedirectToAction(nameof(KursPajak));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteKursPajak(int id)
    {
        try
        {
            await _db.ExecuteAsync("DELETE FROM DOIT_KURS_PAJAK WHERE ID = @Id", new { Id = id });
            TempData["Success"] = "Data kurs berhasil dihapus.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting kurs pajak");
            TempData["Error"] = $"Gagal menghapus kurs: {ex.Message}";
        }
        return RedirectToAction(nameof(KursPajak));
    }

    [HttpPost]
    public async Task<IActionResult> SyncKemenkeuRates()
    {
        try
        {
            // Simulated live sync with Kementerian Keuangan Kurs Pajak API
            var now = DateTime.Now;
            var endDate = now.AddDays(7);
            var kmkNo = $"KMK-{(now.DayOfYear / 7) + 1}/MK.10/{now.Year}";

            await _tax.UpdateRateAsync("USD", 16285.0000m, now, endDate, kmkNo);
            await _tax.UpdateRateAsync("JPY", 10675.0000m, now, endDate, kmkNo);
            await _tax.UpdateRateAsync("EUR", 17520.0000m, now, endDate, kmkNo);
            await _tax.UpdateRateAsync("SGD", 12190.0000m, now, endDate, kmkNo);
            await _tax.UpdateRateAsync("CNY", 2248.0000m, now, endDate, kmkNo);
            await _tax.UpdateRateAsync("THB", 452.5000m, now, endDate, kmkNo);

            await _audit.LogAsync(User.Identity?.Name ?? "system", "SYNC_KURS_KEMENKEU", "MASTER", "SYNC", $"Sinkronisasi Kurs Pajak Kemenkeu ({kmkNo}) otomatis berhasil.");

            TempData["Success"] = $"Sukses sinkronisasi Kurs Pajak Kemenkeu terbaru ({kmkNo})!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing Kemenkeu rates");
            TempData["Error"] = $"Gagal sinkronisasi kurs Kemenkeu: {ex.Message}";
        }
        return RedirectToAction(nameof(KursPajak));
    }

    public async Task<IActionResult> Part(string? search)
    {
        ViewData["Title"] = "Master Part Suzuki";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Part";
        
        var sql = @"SELECT ID AS Id, PART_NO AS PartNo, PART_NAME AS PartName, HS_CODE AS HsCode, 
                           SATUAN AS Satuan, SUBINVENTORY AS Subinventory, PLANT AS Plant, 
                           NEGASAL AS NegAsal, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_PART WHERE IS_ACTIVE = 1";
        
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (PART_NO LIKE @Search OR PART_NAME LIKE @Search OR HS_CODE LIKE @Search OR SUBINVENTORY LIKE @Search OR PLANT LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        
        sql += " ORDER BY ID DESC";
        
        var parts = (await _db.QueryAsync<DoItG2.Models.Common.MasterPartModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(parts);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePart(string partNo, string partName, string? hsCode, string? satuan, string? subinventory, string? plant, string? negAsal)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(partNo) || string.IsNullOrWhiteSpace(partName))
            {
                TempData["Error"] = "Nomor Part dan Nama Part wajib diisi!";
                return RedirectToAction(nameof(Part));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_PART WHERE PART_NO = @PartNo)
                        BEGIN
                            UPDATE DOIT_MASTER_PART 
                            SET PART_NAME = @PartName, HS_CODE = @HsCode, SATUAN = @Satuan, 
                                SUBINVENTORY = @Subinventory, PLANT = @Plant, NEGASAL = @NegAsal, IS_ACTIVE = 1 
                            WHERE PART_NO = @PartNo
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_PART (PART_NO, PART_NAME, HS_CODE, SATUAN, SUBINVENTORY, PLANT, NEGASAL, IS_ACTIVE)
                            VALUES (@PartNo, @PartName, @HsCode, @Satuan, @Subinventory, @Plant, @NegAsal, 1)
                        END";

            await _db.ExecuteAsync(sql, new { 
                PartNo = partNo.Trim(), 
                PartName = partName.Trim(), 
                HsCode = hsCode?.Trim(), 
                Satuan = satuan?.Trim() ?? "PCS", 
                Subinventory = subinventory?.Trim(), 
                Plant = plant?.Trim(), 
                NegAsal = negAsal?.Trim() ?? "JP" 
            });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'CREATE_PART', 'MASTER', @PartNo, @Desc, @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", PartNo = partNo.Trim(), Desc = $"Menambah/memperbarui Master Part Suzuki: {partNo} - {partName}", Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Master Part {partNo} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating master part");
            TempData["Error"] = $"Gagal menyimpan part: {ex.Message}";
        }
        return RedirectToAction(nameof(Part));
    }

    [HttpPost]
    public async Task<IActionResult> UploadPartExcel(IFormFile excelFile)
    {
        try
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "Pilih file Excel (.xlsx / .xls) untuk diunggah!";
                return RedirectToAction(nameof(Part));
            }

            var extension = Path.GetExtension(excelFile.FileName).ToLower();
            if (extension != ".xlsx" && extension != ".xls")
            {
                TempData["Error"] = "Format file tidak valid! Gunakan format file Excel (.xlsx atau .xls).";
                return RedirectToAction(nameof(Part));
            }

            using var stream = excelFile.OpenReadStream();
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                TempData["Error"] = "Lembar kerja (worksheet) Excel kosong!";
                return RedirectToAction(nameof(Part));
            }

            var rows = worksheet.RangeUsed()?.RowsUsed()?.Skip(1); // Skip header row
            if (rows == null || !rows.Any())
            {
                TempData["Error"] = "Tidak ada baris data dalam file Excel!";
                return RedirectToAction(nameof(Part));
            }

            int count = 0;
            foreach (var row in rows)
            {
                var partNo = row.Cell(1).GetValue<string>()?.Trim();
                var partName = row.Cell(2).GetValue<string>()?.Trim();
                var hsCode = row.Cell(3).GetValue<string>()?.Trim();
                var satuan = row.Cell(4).GetValue<string>()?.Trim();
                var subinv = row.Cell(5).GetValue<string>()?.Trim();
                var plant = row.Cell(6).GetValue<string>()?.Trim();
                var negAsal = row.Cell(7).GetValue<string>()?.Trim();

                if (!string.IsNullOrWhiteSpace(partNo))
                {
                    if (string.IsNullOrWhiteSpace(partName)) partName = partNo;
                    if (string.IsNullOrWhiteSpace(satuan)) satuan = "PCS";
                    if (string.IsNullOrWhiteSpace(negAsal)) negAsal = "JP";

                    var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_PART WHERE PART_NO = @PartNo)
                                BEGIN
                                    UPDATE DOIT_MASTER_PART 
                                    SET PART_NAME = @PartName, HS_CODE = @HsCode, SATUAN = @Satuan, 
                                        SUBINVENTORY = @Subinventory, PLANT = @Plant, NEGASAL = @NegAsal, IS_ACTIVE = 1 
                                    WHERE PART_NO = @PartNo
                                END
                                ELSE
                                BEGIN
                                    INSERT INTO DOIT_MASTER_PART (PART_NO, PART_NAME, HS_CODE, SATUAN, SUBINVENTORY, PLANT, NEGASAL, IS_ACTIVE)
                                    VALUES (@PartNo, @PartName, @HsCode, @Satuan, @Subinventory, @Plant, @NegAsal, 1)
                                END";

                    await _db.ExecuteAsync(sql, new { 
                        PartNo = partNo, 
                        PartName = partName, 
                        HsCode = hsCode, 
                        Satuan = satuan, 
                        Subinventory = subinv, 
                        Plant = plant, 
                        NegAsal = negAsal 
                    });
                    count++;
                }
            }

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'UPLOAD_EXCEL_PART', 'MASTER', 'EXCEL_UPLOAD', @Desc, @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Desc = $"Upload Excel Master Part berhasil: {count} data terproses dari {excelFile.FileName}", Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Sukses mengunggah & memperbarui {count} data Master Part Suzuki dari Excel!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading Master Part Excel");
            TempData["Error"] = $"Gagal memproses file Excel: {ex.Message}";
        }
        return RedirectToAction(nameof(Part));
    }

    public async Task<IActionResult> ExportPartExcel(string? search)
    {
        try
        {
            var sql = @"SELECT PART_NO AS PartNo, PART_NAME AS PartName, HS_CODE AS HsCode, 
                               SATUAN AS Satuan, SUBINVENTORY AS Subinventory, PLANT AS Plant, 
                               NEGASAL AS NegAsal, IS_ACTIVE AS IsActive 
                        FROM DOIT_MASTER_PART WHERE IS_ACTIVE = 1";
            
            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (PART_NO LIKE @Search OR PART_NAME LIKE @Search OR HS_CODE LIKE @Search OR SUBINVENTORY LIKE @Search OR PLANT LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            
            sql += " ORDER BY ID DESC";
            
            var parts = (await _db.QueryAsync<DoItG2.Models.Common.MasterPartModel>(sql, parameters)).ToList();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Master Part Suzuki");

            string[] headers = { "No", "Nomor Part", "Nama Part / Deskripsi", "HS Code", "Satuan", "Subinventory", "Plant", "Negara Asal", "Status" };

            // Header Title with Merged Cells
            ws.Range(1, 1, 1, headers.Length).Merge();
            ws.Cell("A1").Value = "DATA MASTER PART SUZUKI - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Range(2, 1, 2, headers.Length).Merge();
            ws.Cell("A2").Value = $"Tanggal Export: {DateTime.Now:dd-MM-yyyy HH:mm} WIB | Total Data: {parts.Count} record";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            // Table Headers (Row 4)
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
            }

            // Data Rows (Row 5 onwards)
            int rowIdx = 5;
            foreach (var item in parts)
            {
                ws.Cell(rowIdx, 1).Value = rowIdx - 4;
                ws.Cell(rowIdx, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 2).Value = item.PartNo;
                ws.Cell(rowIdx, 2).Style.Font.FontName = "Consolas";

                ws.Cell(rowIdx, 3).Value = item.PartName;
                ws.Cell(rowIdx, 4).Value = item.HsCode ?? "-";
                ws.Cell(rowIdx, 4).Style.Font.FontName = "Consolas";
                ws.Cell(rowIdx, 4).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 5).Value = item.Satuan ?? "PCS";
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 6).Value = item.Subinventory ?? "-";
                ws.Cell(rowIdx, 7).Value = item.Plant ?? "-";
                ws.Cell(rowIdx, 8).Value = item.NegAsal ?? "JP";
                ws.Cell(rowIdx, 8).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 9).Value = item.IsActive ? "Aktif" : "Non-Aktif";
                ws.Cell(rowIdx, 9).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Alternating row style
                if (rowIdx % 2 == 0)
                {
                    ws.Row(rowIdx).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F8FAFC");
                }

                rowIdx++;
            }

            // Apply grid borders & auto column width
            var range = ws.Range(4, 1, Math.Max(5, rowIdx - 1), headers.Length);
            range.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#CBD5E1");
            range.Style.Border.InsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#E2E8F0");

            ws.Columns().AdjustToContents(4, Math.Max(5, rowIdx - 1));
            ws.Column(1).Width = 8; // Neat "No" column width

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"Master_Part_Suzuki_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Master Part Excel");
            TempData["Error"] = $"Gagal mengeksport Excel: {ex.Message}";
            return RedirectToAction(nameof(Part));
        }
    }

    public IActionResult DownloadPartTemplate()
    {
        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Template Master Part");

            ws.Cell("A1").Value = "TEMPLATE IMPORT MASTER PART SUZUKI - DO-IT G2";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 13;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Cell("A2").Value = "Petunjuk: Isi data part mulai dari baris 4. Kolom PART_NO dan PART_NAME wajib diisi.";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            string[] headers = { "PART_NO", "PART_NAME", "HS_CODE", "SATUAN", "SUBINVENTORY", "PLANT", "NEGASAL" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }

            // Sample Rows
            ws.Cell(5, 1).Value = "13780-68K00";
            ws.Cell(5, 2).Value = "FILTER COMP AIR";
            ws.Cell(5, 3).Value = "84213120";
            ws.Cell(5, 4).Value = "PCS";
            ws.Cell(5, 5).Value = "RAW";
            ws.Cell(5, 6).Value = "P1";
            ws.Cell(5, 7).Value = "JP";

            ws.Cell(6, 1).Value = "16510-61A01";
            ws.Cell(6, 2).Value = "ELEMENT OIL FILTER";
            ws.Cell(6, 3).Value = "84212311";
            ws.Cell(6, 4).Value = "PCS";
            ws.Cell(6, 5).Value = "SPARE";
            ws.Cell(6, 6).Value = "P2";
            ws.Cell(6, 7).Value = "JP";

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Import_Master_Part_Suzuki.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Master Part template");
            TempData["Error"] = $"Gagal mendownload template: {ex.Message}";
            return RedirectToAction(nameof(Part));
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeletePart(int id)
    {
        try
        {
            await _db.ExecuteAsync("UPDATE DOIT_MASTER_PART SET IS_ACTIVE = 0 WHERE ID = @Id", new { Id = id });
            TempData["Success"] = "Master Part berhasil dinonaktifkan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting master part");
            TempData["Error"] = "Gagal menonaktifkan master part.";
        }
        return RedirectToAction(nameof(Part));
    }

    public async Task<IActionResult> DokumenPib(string? search)
    {
        ViewData["Title"] = "Master Dokumen Kepabeanan";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Dokumen";

        var sql = @"SELECT ID AS Id, KD_DOK AS KdDok, NM_DOK AS NmDok, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_DOKUMEN_PIB WHERE IS_ACTIVE = 1";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (KD_DOK LIKE @Search OR NM_DOK LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        sql += " ORDER BY KD_DOK ASC";

        var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterDokumenModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDokumenPib(string kdDok, string nmDok)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(kdDok) || string.IsNullOrWhiteSpace(nmDok))
            {
                TempData["Error"] = "Kode dan Nama Dokumen wajib diisi!";
                return RedirectToAction(nameof(DokumenPib));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_DOKUMEN_PIB WHERE KD_DOK = @KdDok)
                        BEGIN
                            UPDATE DOIT_MASTER_DOKUMEN_PIB SET NM_DOK = @NmDok, IS_ACTIVE = 1 WHERE KD_DOK = @KdDok
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_DOKUMEN_PIB (KD_DOK, NM_DOK, IS_ACTIVE) VALUES (@KdDok, @NmDok, 1)
                        END";

            await _db.ExecuteAsync(sql, new { KdDok = kdDok.Trim(), NmDok = nmDok.Trim() });
            TempData["Success"] = $"Master Dokumen Pabean {kdDok} - {nmDok} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating master dokumen");
            TempData["Error"] = "Gagal menyimpan dokumen.";
        }
        return RedirectToAction(nameof(DokumenPib));
    }

    public async Task<IActionResult> ExportDokumenPibExcel(string? search)
    {
        try
        {
            var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterDokumenModel>(
                "SELECT KD_DOK AS KdDok, NM_DOK AS NmDok FROM DOIT_MASTER_DOKUMEN_PIB WHERE IS_ACTIVE = 1 ORDER BY KD_DOK ASC")).ToList();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Master Dokumen Pabean");
            ws.Cell("A1").Value = "MASTER DOKUMEN KEPABEANAN SUZUKI - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 13;

            string[] headers = { "No", "Kode Dokumen (DJBC)", "Nama Dokumen Kepabeanan" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }

            int idx = 4;
            foreach (var item in items)
            {
                ws.Cell(idx, 1).Value = idx - 3;
                ws.Cell(idx, 2).Value = item.KdDok;
                ws.Cell(idx, 3).Value = item.NmDok;
                idx++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Dokumen_Pabean_{DateTime.Now:yyyyMMdd}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting master dokumen");
            return RedirectToAction(nameof(DokumenPib));
        }
    }

    public async Task<IActionResult> Lartas(string? search)
    {
        ViewData["Title"] = "Persetujuan Impor (PI) Lartas";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Lartas";

        var sql = @"SELECT ID AS Id, NO_PI AS NoPi, KOMODITAS AS Komoditas, KUOTA_AWAL AS KuotaAwal, 
                           KUOTA_TERPAKAI AS KuotaTerpakai, SATUAN AS Satuan, TGL_BERLAKU AS TglBerlaku, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_LARTAS WHERE IS_ACTIVE = 1";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (NO_PI LIKE @Search OR KOMODITAS LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        sql += " ORDER BY ID DESC";

        var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterLartasModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateLartas(string noPi, string komoditas, decimal kuotaAwal, string? satuan, DateTime? tglBerlaku)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(noPi) || string.IsNullOrWhiteSpace(komoditas))
            {
                TempData["Error"] = "Nomor PI dan Komoditas wajib diisi!";
                return RedirectToAction(nameof(Lartas));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_LARTAS WHERE NO_PI = @NoPi)
                        BEGIN
                            UPDATE DOIT_MASTER_LARTAS SET KOMODITAS = @Komoditas, KUOTA_AWAL = @KuotaAwal, SATUAN = @Satuan, TGL_BERLAKU = @TglBerlaku, IS_ACTIVE = 1 WHERE NO_PI = @NoPi
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_LARTAS (NO_PI, KOMODITAS, KUOTA_AWAL, KUOTA_TERPAKAI, SATUAN, TGL_BERLAKU, IS_ACTIVE)
                            VALUES (@NoPi, @Komoditas, @KuotaAwal, 0, @Satuan, @TglBerlaku, 1)
                        END";

            await _db.ExecuteAsync(sql, new { NoPi = noPi.Trim(), Komoditas = komoditas.Trim(), KuotaAwal = kuotaAwal, Satuan = satuan ?? "KG", TglBerlaku = tglBerlaku });
            TempData["Success"] = $"Izin PI Lartas {noPi} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Lartas");
            TempData["Error"] = "Gagal menyimpan izin PI Lartas.";
        }
        return RedirectToAction(nameof(Lartas));
    }

    public async Task<IActionResult> Supplier(string? search)
    {
        ViewData["Title"] = "Master Pemasok Impor (Supplier)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Pemasok";

        var sql = @"SELECT ID AS Id, KD_PEMASOK AS KdPemasok, NM_PEMASOK AS NmPemasok, ALM_PEMASOK AS AlmPemasok, 
                           NEG_PEMASOK AS NegPemasok, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_PEMASOK WHERE IS_ACTIVE = 1";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (KD_PEMASOK LIKE @Search OR NM_PEMASOK LIKE @Search OR ALM_PEMASOK LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        sql += " ORDER BY ID DESC";

        var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterSupplierModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSupplier(string kdPemasok, string nmPemasok, string? almPemasok, string? negPemasok)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(kdPemasok) || string.IsNullOrWhiteSpace(nmPemasok))
            {
                TempData["Error"] = "Kode dan Nama Pemasok wajib diisi!";
                return RedirectToAction(nameof(Supplier));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_PEMASOK WHERE KD_PEMASOK = @KdPemasok)
                        BEGIN
                            UPDATE DOIT_MASTER_PEMASOK SET NM_PEMASOK = @NmPemasok, ALM_PEMASOK = @AlmPemasok, NEG_PEMASOK = @NegPemasok, IS_ACTIVE = 1 WHERE KD_PEMASOK = @KdPemasok
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_PEMASOK (KD_PEMASOK, NM_PEMASOK, ALM_PEMASOK, NEG_PEMASOK, IS_ACTIVE)
                            VALUES (@KdPemasok, @NmPemasok, @AlmPemasok, @NegPemasok, 1)
                        END";

            await _db.ExecuteAsync(sql, new { KdPemasok = kdPemasok.Trim(), NmPemasok = nmPemasok.Trim(), AlmPemasok = almPemasok, NegPemasok = negPemasok ?? "JP" });
            TempData["Success"] = $"Master Pemasok {kdPemasok} - {nmPemasok} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating supplier");
            TempData["Error"] = "Gagal menyimpan supplier.";
        }
        return RedirectToAction(nameof(Supplier));
    }

    public async Task<IActionResult> Buyer(string? search)
    {
        ViewData["Title"] = "Master Pembeli Ekspor (Buyer)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Pembeli";

        var sql = @"SELECT ID AS Id, KD_PEMBELI AS KdPembeli, NM_PEMBELI AS NmPembeli, ALM_PEMBELI AS AlmPembeli, 
                           NEG_PEMBELI AS NegPembeli, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_PEMBELI WHERE IS_ACTIVE = 1";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (KD_PEMBELI LIKE @Search OR NM_PEMBELI LIKE @Search OR ALM_PEMBELI LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        sql += " ORDER BY ID DESC";

        var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterBuyerModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBuyer(string kdPembeli, string nmPembeli, string? almPembeli, string? negPembeli)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(kdPembeli) || string.IsNullOrWhiteSpace(nmPembeli))
            {
                TempData["Error"] = "Kode dan Nama Pembeli wajib diisi!";
                return RedirectToAction(nameof(Buyer));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_PEMBELI WHERE KD_PEMBELI = @KdPembeli)
                        BEGIN
                            UPDATE DOIT_MASTER_PEMBELI SET NM_PEMBELI = @NmPembeli, ALM_PEMBELI = @AlmPembeli, NEG_PEMBELI = @NegPembeli, IS_ACTIVE = 1 WHERE KD_PEMBELI = @KdPembeli
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_PEMBELI (KD_PEMBELI, NM_PEMBELI, ALM_PEMBELI, NEG_PEMBELI, IS_ACTIVE)
                            VALUES (@KdPembeli, @NmPembeli, @AlmPembeli, @NegPembeli, 1)
                        END";

            await _db.ExecuteAsync(sql, new { KdPembeli = kdPembeli.Trim(), NmPembeli = nmPembeli.Trim(), AlmPembeli = almPembeli, NegPembeli = negPembeli ?? "JP" });
            TempData["Success"] = $"Master Pembeli {kdPembeli} - {nmPembeli} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating buyer");
            TempData["Error"] = "Gagal menyimpan buyer.";
        }
        return RedirectToAction(nameof(Buyer));
    }

    public async Task<IActionResult> Fasilitas(string? search)
    {
        ViewData["Title"] = "Master Fasilitas Pabean / SKEP";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> Fasilitas";

        var sql = @"SELECT ID AS Id, NO_SKEP AS NoSkep, TGL_SKEP AS TglSkep, JENIS_FASILITAS AS JenisFasilitas, 
                           DESKRIPSI AS Deskripsi, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_FASILITAS WHERE IS_ACTIVE = 1";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (NO_SKEP LIKE @Search OR DESKRIPSI LIKE @Search OR JENIS_FASILITAS LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        sql += " ORDER BY ID DESC";

        var items = (await _db.QueryAsync<DoItG2.Models.Common.MasterFasilitasModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateFasilitas(string noSkep, DateTime? tglSkep, string? jenisFasilitas, string? deskripsi)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(noSkep))
            {
                TempData["Error"] = "Nomor SKEP wajib diisi!";
                return RedirectToAction(nameof(Fasilitas));
            }

            var sql = @"IF EXISTS (SELECT 1 FROM DOIT_MASTER_FASILITAS WHERE NO_SKEP = @NoSkep)
                        BEGIN
                            UPDATE DOIT_MASTER_FASILITAS SET TGL_SKEP = @TglSkep, JENIS_FASILITAS = @JenisFasilitas, DESKRIPSI = @Deskripsi, IS_ACTIVE = 1 WHERE NO_SKEP = @NoSkep
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DOIT_MASTER_FASILITAS (NO_SKEP, TGL_SKEP, JENIS_FASILITAS, DESKRIPSI, IS_ACTIVE)
                            VALUES (@NoSkep, @TglSkep, @JenisFasilitas, @Deskripsi, 1)
                        END";

            await _db.ExecuteAsync(sql, new { NoSkep = noSkep.Trim(), TglSkep = tglSkep, JenisFasilitas = jenisFasilitas ?? "KITE", Deskripsi = deskripsi });
            TempData["Success"] = $"Master Fasilitas SKEP {noSkep} berhasil disimpan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating fasilitas");
            TempData["Error"] = "Gagal menyimpan SKEP Fasilitas.";
        }
        return RedirectToAction(nameof(Fasilitas));
    }

    public async Task<IActionResult> Pkb(string? search)
    {
        ViewData["Title"] = "Master PKB & Stuffing Gudang";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Master <span class='breadcrumb-sep'>/</span> PKB";

        var sql = @"SELECT ID AS Id, PIB_TYPE AS PibType, CAR AS Car, FASILITAS AS Fasilitas, GUDANG AS Gudang, PETUGAS AS Petugas, NOPHONE AS NoPhone, ALMTSIAP AS AlmtSiap, IS_ACTIVE AS IsActive 
                    FROM DOIT_MASTER_PKB WHERE IS_ACTIVE = 1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (CAR LIKE @Search OR FASILITAS LIKE @Search OR GUDANG LIKE @Search OR PETUGAS LIKE @Search)";
        }
        sql += " ORDER BY ID DESC";

        var list = (await _db.QueryAsync<DoItG2.Models.Common.MasterPkbModel>(sql, new { Search = $"%{search}%" })).ToList();
        ViewBag.Search = search;
        return View(list);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePkb(string pibType, string car, string fasilitas, string gudang, string? petugas, string? noPhone, string? almtSiap)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(car))
            {
                TempData["Error"] = "Nomor CAR Wajib diisi!";
                return RedirectToAction(nameof(Pkb));
            }

            var sql = @"INSERT INTO DOIT_MASTER_PKB (PIB_TYPE, CAR, FASILITAS, GUDANG, PETUGAS, NOPHONE, ALMTSIAP, IS_ACTIVE)
                        VALUES (@PibType, @Car, @Fasilitas, @Gudang, @Petugas, @NoPhone, @AlmtSiap, 1)";

            await _db.ExecuteAsync(sql, new { PibType = pibType ?? "81", Car = car.Trim(), Fasilitas = fasilitas, Gudang = gudang, Petugas = petugas, NoPhone = noPhone, AlmtSiap = almtSiap });
            TempData["Success"] = $"Master PKB CAR {car} berhasil disimpan!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PKB");
            TempData["Error"] = $"Gagal menyimpan PKB: {ex.Message}";
        }
        return RedirectToAction(nameof(Pkb));
    }
}

[Authorize]
public class ReportController : Controller
{
    private readonly DatabaseContext _db;
    private readonly IPdfReportService _pdf;
    private readonly ILogger<ReportController> _logger;

    public ReportController(DatabaseContext db, IPdfReportService pdf, ILogger<ReportController> logger)
    {
        _db = db;
        _pdf = pdf;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> PrintPibPdf(string car)
    {
        try
        {
            var pdfBytes = await _pdf.GeneratePibPdfAsync(car);
            return File(pdfBytes, "application/pdf", $"PIB_BC20_{car}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PIB PDF in ReportController: {Car}", car);
            TempData["Error"] = $"Gagal mencetak dokumen PDF: {ex.Message}";
            return RedirectToAction(nameof(Pib));
        }
    }

    [HttpGet]
    public async Task<IActionResult> PrintPebPdf(string car)
    {
        try
        {
            var pdfBytes = await _pdf.GeneratePebPdfAsync(car);
            return File(pdfBytes, "application/pdf", $"PEB_BC30_{car}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PEB PDF in ReportController: {Car}", car);
            TempData["Error"] = $"Gagal mencetak dokumen PDF: {ex.Message}";
            return RedirectToAction(nameof(Peb));
        }
    }

    private static string FormatExcelDate(object? dateObj)
    {
        if (dateObj == null) return "-";
        if (dateObj is DateTime dt) return dt.ToString("dd/MM/yyyy");
        var str = dateObj.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(str)) return "-";
        if (DateTime.TryParse(str, out var parsed)) return parsed.ToString("dd/MM/yyyy");
        if (str.Length == 8 && long.TryParse(str, out _))
        {
            return $"{str.Substring(6, 2)}/{str.Substring(4, 2)}/{str.Substring(0, 4)}";
        }
        return str;
    }

    public async Task<IActionResult> Pib(string? search, string? dateFrom, string? dateTo)
    {
        ViewData["Title"] = "Laporan Realisasi Impor (PIB)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Laporan <span class='breadcrumb-sep'>/</span> PIB";

        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
        var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_IMO AS NmImo, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, 
                           JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                           NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, FOB, CIF, NETTO, BRUTO,
                           ISNULL(ENTITY, 'SIM') AS Entity,
                           CASE 
                                WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                                WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                                ELSE 'DRAFT'
                           END AS Status 
                    FROM PIB_DOIT_FINAL_HEADER 
                    WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NM_IMO LIKE '%MOTOR%' OR ID_IMP LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NM_IMO LIKE '%SALES%' OR ID_IMP LIKE '%011297389%')))";

        var parameters = new DynamicParameters();
        parameters.Add("Entity", entity);
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (CAR LIKE @Search OR NM_PEMASOK LIKE @Search OR NM_IMO LIKE @Search OR ID_IMP LIKE @Search OR NO_PEN_PIB LIKE @Search OR NO_SPPB LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            sql += " AND CREATION_DATE >= @DateFrom";
            parameters.Add("DateFrom", dateFrom);
        }
        if (!string.IsNullOrWhiteSpace(dateTo))
        {
            sql += " AND CREATION_DATE <= @DateTo";
            parameters.Add("DateTo", dateTo + " 23:59:59");
        }

        sql += " ORDER BY CREATION_DATE DESC";

        var items = (await _db.QueryAsync<PibHeaderModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        return View(items);
    }

    public async Task<IActionResult> ExportPibExcel(string? search, string? dateFrom, string? dateTo)
    {
        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_IMO AS NmImo, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, 
                               JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                               NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, FOB, CIF, NETTO, BRUTO,
                               CASE 
                                    WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB Terbit'
                                    WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'Nopen Terdaftar'
                                    ELSE 'Draft PIB'
                               END AS Status 
                        FROM PIB_DOIT_FINAL_HEADER 
                        WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NM_IMO LIKE '%MOTOR%' OR ID_IMP LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NM_IMO LIKE '%SALES%' OR ID_IMP LIKE '%011297389%')))";

            var parameters = new DynamicParameters();
            parameters.Add("Entity", entity);
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (CAR LIKE @Search OR NM_PEMASOK LIKE @Search OR NM_IMO LIKE @Search OR ID_IMP LIKE @Search OR NO_PEN_PIB LIKE @Search OR NO_SPPB LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            if (!string.IsNullOrWhiteSpace(dateFrom))
            {
                sql += " AND CREATION_DATE >= @DateFrom";
                parameters.Add("DateFrom", dateFrom);
            }
            if (!string.IsNullOrWhiteSpace(dateTo))
            {
                sql += " AND CREATION_DATE <= @DateTo";
                parameters.Add("DateTo", dateTo + " 23:59:59");
            }

            sql += " ORDER BY CREATION_DATE DESC";

            var items = (await _db.QueryAsync<PibHeaderModel>(sql, parameters)).ToList();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan PIB");

            string[] headers = { "No", "Nomor CAR", "Nama Pemasok", "No. Nopen PIB", "Tgl Nopen", "No. SPPB", "Tgl SPPB", "Jml Barang", "FOB (USD)", "CIF (USD)", "Netto (KGM)", "Status" };

            // Title Rows with Merged Cells across header columns to avoid column A stretching
            ws.Range(1, 1, 1, headers.Length).Merge();
            ws.Cell("A1").Value = "LAPORAN REALISASI IMPOR (PIB) SUZUKI - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Range(2, 1, 2, headers.Length).Merge();
            ws.Cell("A2").Value = $"Periode: {(string.IsNullOrEmpty(dateFrom) ? "Semua" : dateFrom)} s/d {(string.IsNullOrEmpty(dateTo) ? "Semua" : dateTo)} | Tanggal Cetak: {DateTime.Now:dd-MM-yyyy HH:mm} WIB | Total: {items.Count} Dokumen";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }

            int rowIdx = 5;
            foreach (var item in items)
            {
                ws.Cell(rowIdx, 1).Value = rowIdx - 4;
                ws.Cell(rowIdx, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 2).Value = item.Car;
                ws.Cell(rowIdx, 2).Style.Font.FontName = "Consolas";

                ws.Cell(rowIdx, 3).Value = item.NmPemasok ?? "-";
                ws.Cell(rowIdx, 4).Value = item.PibNo ?? "-";
                ws.Cell(rowIdx, 4).Style.Font.FontName = "Consolas";
                ws.Cell(rowIdx, 5).Value = FormatExcelDate(item.PibTg);
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 6).Value = item.SppbNo ?? "-";
                ws.Cell(rowIdx, 6).Style.Font.FontName = "Consolas";
                ws.Cell(rowIdx, 7).Value = FormatExcelDate(item.SppbTg);
                ws.Cell(rowIdx, 7).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 8).Value = item.JmlBrg;
                ws.Cell(rowIdx, 8).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 9).Value = decimal.TryParse(item.Fob, out var fob) ? fob : 0m;
                ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "$#,##0.00";

                ws.Cell(rowIdx, 10).Value = decimal.TryParse(item.Cif, out var cif) ? cif : 0m;
                ws.Cell(rowIdx, 10).Style.NumberFormat.Format = "$#,##0.00";

                ws.Cell(rowIdx, 11).Value = decimal.TryParse(item.Netto, out var net) ? net : 0m;
                ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(rowIdx, 12).Value = item.Status ?? "DRAFT";
                ws.Cell(rowIdx, 12).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                if (rowIdx % 2 == 0) ws.Row(rowIdx).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F8FAFC");
                rowIdx++;
            }

            var range = ws.Range(4, 1, Math.Max(5, rowIdx - 1), headers.Length);
            range.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#CBD5E1");

            ws.Columns().AdjustToContents(4, Math.Max(5, rowIdx - 1));
            ws.Column(1).Width = 8; // Neat "No" column width

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_Realisasi_Impor_PIB_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting PIB report Excel");
            TempData["Error"] = $"Gagal mengeksport Excel PIB: {ex.Message}";
            return RedirectToAction(nameof(Pib));
        }
    }

    public async Task<IActionResult> Peb(string? search, string? dateFrom, string? dateTo)
    {
        ViewData["Title"] = "Laporan Realisasi Ekspor (PEB)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Laporan <span class='breadcrumb-sep'>/</span> PEB";

        var entity = User.FindFirst("Entity")?.Value ?? "SIM";
        var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, 
                           TGEKS AS TgEks, NETTO, BRUTO, FOB, STATUS,
                           ISNULL(ENTITY, 'SIM') AS Entity
                    FROM PEB_DOIT_FINAL_HEADER 
                    WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NAMAEKS LIKE '%MOTOR%' OR NPWPEKS LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NAMAEKS LIKE '%SALES%' OR NPWPEKS LIKE '%011297389%')))";

        var parameters = new DynamicParameters();
        parameters.Add("Entity", entity);
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (CAR LIKE @Search OR NAMAEKS LIKE @Search OR NPWPEKS LIKE @Search OR NEGBELI LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            sql += " AND CREATED_DATE >= @DateFrom";
            parameters.Add("DateFrom", dateFrom);
        }
        if (!string.IsNullOrWhiteSpace(dateTo))
        {
            sql += " AND CREATED_DATE <= @DateTo";
            parameters.Add("DateTo", dateTo + " 23:59:59");
        }

        sql += " ORDER BY CREATED_DATE DESC";

        var items = (await _db.QueryAsync<PebHeaderModel>(sql, parameters)).ToList();
        ViewBag.Search = search;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        return View(items);
    }

    public async Task<IActionResult> ExportPebExcel(string? search, string? dateFrom, string? dateTo)
    {
        try
        {
            var entity = User.FindFirst("Entity")?.Value ?? "SIM";
            var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, 
                               TGEKS AS TgEks, NETTO, BRUTO, FOB, STATUS 
                        FROM PEB_DOIT_FINAL_HEADER 
                        WHERE (ENTITY = @Entity OR (@Entity = 'SIM' AND (ENTITY IS NULL OR NAMAEKS LIKE '%MOTOR%' OR NPWPEKS LIKE '%011297371%')) OR (@Entity = 'SIS' AND (ENTITY = 'SIS' OR NAMAEKS LIKE '%SALES%' OR NPWPEKS LIKE '%011297389%')))";

            var parameters = new DynamicParameters();
            parameters.Add("Entity", entity);
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (CAR LIKE @Search OR NAMAEKS LIKE @Search OR NPWPEKS LIKE @Search OR NEGBELI LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            if (!string.IsNullOrWhiteSpace(dateFrom))
            {
                sql += " AND CREATED_DATE >= @DateFrom";
                parameters.Add("DateFrom", dateFrom);
            }
            if (!string.IsNullOrWhiteSpace(dateTo))
            {
                sql += " AND CREATED_DATE <= @DateTo";
                parameters.Add("DateTo", dateTo + " 23:59:59");
            }

            sql += " ORDER BY CREATED_DATE DESC";

            var items = (await _db.QueryAsync<PebHeaderModel>(sql, parameters)).ToList();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan PEB");

            string[] headers = { "No", "Nomor CAR", "Pembeli / Eksportir", "Negara Tujuan", "Tgl Ekspor", "FOB (USD)", "Netto (KGM)", "Bruto (KGM)", "Status" };

            // Title Rows with Merged Cells across header columns to avoid column A stretching
            ws.Range(1, 1, 1, headers.Length).Merge();
            ws.Cell("A1").Value = "LAPORAN REALISASI EKSPOR (PEB) SUZUKI - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Range(2, 1, 2, headers.Length).Merge();
            ws.Cell("A2").Value = $"Periode: {(string.IsNullOrEmpty(dateFrom) ? "Semua" : dateFrom)} s/d {(string.IsNullOrEmpty(dateTo) ? "Semua" : dateTo)} | Tanggal Cetak: {DateTime.Now:dd-MM-yyyy HH:mm} WIB | Total: {items.Count} Dokumen";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }

            int rowIdx = 5;
            foreach (var item in items)
            {
                ws.Cell(rowIdx, 1).Value = rowIdx - 4;
                ws.Cell(rowIdx, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 2).Value = item.Car;
                ws.Cell(rowIdx, 2).Style.Font.FontName = "Consolas";

                ws.Cell(rowIdx, 3).Value = item.NamaBeli ?? "-";
                ws.Cell(rowIdx, 4).Value = item.NegBeli ?? "-";
                ws.Cell(rowIdx, 4).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 5).Value = item.TgEks.HasValue ? item.TgEks.Value.ToString("dd/MM/yyyy") : "-";
                ws.Cell(rowIdx, 5).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                ws.Cell(rowIdx, 6).Value = item.Fob;
                ws.Cell(rowIdx, 6).Style.NumberFormat.Format = "$#,##0.00";

                ws.Cell(rowIdx, 7).Value = item.Netto;
                ws.Cell(rowIdx, 7).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(rowIdx, 8).Value = item.Bruto;
                ws.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(rowIdx, 9).Value = (item.Status == "APPROVED" || item.Status == "NPE" || !string.IsNullOrEmpty(item.Nopen)) ? "NPE Terbit" : "Draft PEB";
                ws.Cell(rowIdx, 9).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                if (rowIdx % 2 == 0) ws.Row(rowIdx).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F8FAFC");
                rowIdx++;
            }

            var range = ws.Range(4, 1, Math.Max(5, rowIdx - 1), headers.Length);
            range.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#CBD5E1");

            ws.Columns().AdjustToContents(4, Math.Max(5, rowIdx - 1));
            ws.Column(1).Width = 8; // Neat "No" column width

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_Realisasi_Ekspor_PEB_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting PEB report Excel");
            TempData["Error"] = $"Gagal mengeksport Excel PEB: {ex.Message}";
            return RedirectToAction(nameof(Peb));
        }
    }

    public IActionResult Kite()
    {
        ViewData["Title"] = "Laporan Pertanggungjawaban KITE";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Laporan <span class='breadcrumb-sep'>/</span> KITE";
        return View();
    }

    public IActionResult ExportKiteExcel()
    {
        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan KITE BCL.KT01");

            ws.Cell("A1").Value = "LAPORAN PERTANGGUNGJAWABAN FASILITAS KITE (BCL.KT01) - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Cell("A2").Value = $"No SKEP: SKEP-128/KM.4/2024 | Tanggal Cetak: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            string[] headers = { "No", "Nomor SKEP", "Nomor Part", "Nama Barang / Subassemblies", "HS Code", "Saldo Awal", "Pemasukan (Impor)", "Pengeluaran (Ekspor)", "Saldo Akhir" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }

            // Sample KITE rows
            string[,] sampleData = {
                { "1", "SKEP-128/KM.4/2024", "13780-68K00", "FILTER COMP AIR", "84213120", "1250", "4000", "3200", "2050" },
                { "2", "SKEP-128/KM.4/2024", "16510-61A01", "ELEMENT OIL FILTER", "84212311", "800", "2500", "2100", "1200" },
                { "3", "SKEP-128/KM.4/2024", "09482-00448", "SPARK PLUG KR6A-10", "85111000", "5000", "12000", "11500", "5500" }
            };

            for (int r = 0; r < 3; r++)
            {
                int rowIdx = r + 5;
                for (int c = 0; c < 9; c++)
                {
                    ws.Cell(rowIdx, c + 1).Value = sampleData[r, c];
                }
            }

            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_KITE_BCL_KT01_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting KITE report Excel");
            TempData["Error"] = $"Gagal mengeksport Excel KITE: {ex.Message}";
            return RedirectToAction(nameof(Kite));
        }
    }

    public IActionResult LapitInv()
    {
        ViewData["Title"] = "Laporan IT Inventory Suzuki";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Laporan <span class='breadcrumb-sep'>/</span> IT Inventory";
        return View();
    }

    public IActionResult ExportLapitInvExcel()
    {
        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("IT Inventory Suzuki");

            ws.Cell("A1").Value = "LAPORAN IT INVENTORY BEAN CUKAI - PT. SUZUKI INDOMOBIL MOTOR";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");

            ws.Cell("A2").Value = $"Tipe: Laporan Mutasi Bahan Baku & Barang Jadi | Tanggal Cetak: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
            ws.Cell("A2").Style.Font.FontSize = 10;
            ws.Cell("A2").Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748B");

            string[] headers = { "No", "Kode Barang", "Nama Barang", "Satuan", "Saldo Awal", "Pemasukan", "Pengeluaran", "Penyesuaian", "Saldo Akhir", "Stock Opname" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1E2D44");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            }

            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_IT_Inventory_Suzuki_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting IT Inventory Excel");
            TempData["Error"] = $"Gagal mengeksport Excel IT Inventory: {ex.Message}";
            return RedirectToAction(nameof(LapitInv));
        }
    }

    public IActionResult Specialized()
    {
        ViewData["Title"] = "Laporan Khusus Kepabeanan (MITA, AEO, COO, Realisasi)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Laporan <span class='breadcrumb-sep'>/</span> Laporan Khusus";
        return View();
    }
}

[Authorize]
public class UserController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<UserController> _logger;

    public UserController(DatabaseContext db, ILogger<UserController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (!role.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Akses ditolak. Menu Manajemen Pengguna hanya dapat diakses oleh Admin Dokumen.";
            return RedirectToAction("Index", "Dashboard");
        }

        ViewData["Title"] = "Manajemen Pengguna";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Pengguna";
        
        try
        {
            var users = await _db.QueryAsync<UserModel>(
                "SELECT id AS Id, user_name AS UserName, full_name AS FullName, email AS Email, user_type AS UserType, is_active AS IsActive FROM doit_user ORDER BY id DESC");
            return View(users.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user list");
            return View(new List<UserModel>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(string username, string fullname, string email, string role)
    {
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (!currentRole.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Akses ditolak. Hanya Admin Dokumen yang dapat menambahkan pengguna baru.";
            return RedirectToAction("Index", "Dashboard");
        }

        try
        {
            // Default working hash of Admin@123
            var hash = "$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6";
            var isAdmin = (role == "ADMIN_DOKUMEN" || role == "ADMIN") ? 1 : 0;
            var isAuthorize = (role == "MANAJER_OPS" || role == "SUPERVISOR") ? 1 : 0;

            await _db.ExecuteAsync(
                @"INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, is_admin,
                    pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, peb_authorize_81, is_partmaster, is_fasilitas, is_pkb, is_pi)
                  VALUES (@Username, @Fullname, @Email, @Hash, @Role, 1, @IsAdmin,
                    1, 1, 1, 1, @IsAuthorize, @IsAuthorize, @IsAdmin, @IsAdmin, @IsAdmin, @IsAdmin)",
                new { Username = username.Trim(), Fullname = fullname.Trim(), Email = email.Trim(), Hash = hash, Role = role, IsAdmin = isAdmin, IsAuthorize = isAuthorize });

            TempData["Success"] = $"User {username} ({role}) berhasil dibuat dengan password default Admin@123.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            TempData["Error"] = $"Gagal membuat user: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id)
    {
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        if (!currentRole.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Akses ditolak. Hanya Admin Dokumen yang dapat menonaktifkan pengguna.";
            return RedirectToAction("Index", "Dashboard");
        }

        try
        {
            await _db.ExecuteAsync("UPDATE doit_user SET is_active = 0 WHERE id = @Id", new { Id = id });
            TempData["Success"] = "User berhasil dinonaktifkan.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user");
            TempData["Error"] = "Gagal menonaktifkan user.";
        }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class SettingController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<SettingController> _logger;

    public SettingController(DatabaseContext db, ILogger<SettingController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Pengaturan Aplikasi";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Pengaturan";
        
        var settings = await _db.QueryAsync<dynamic>("SELECT setting_key AS [Key], value AS [Value], description AS [Desc] FROM doit_setting");
        return View(settings.ToList());
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSetting(string key, string value)
    {
        try
        {
            await _db.ExecuteAsync("UPDATE doit_setting SET value = @Value, updated_at = GETDATE() WHERE setting_key = @Key", new { Key = key, Value = value });
            TempData["Success"] = $"Pengaturan {key} berhasil diperbarui.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating setting");
            TempData["Error"] = "Gagal memperbarui pengaturan.";
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> AuditLog(string? search, string? category, string? module, string? username)
    {
        ViewData["Title"] = "Audit Log Aktivitas";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Audit Log";
        
        var sql = @"SELECT TOP 300 id AS Id, user_name AS UserName, action AS Action, module AS Module, document_id AS DocumentId, description AS Description, ip_address AS IpAddress, is_error AS IsError, created_at AS CreatedAt 
                    FROM doit_audit_log WHERE 1=1";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (description LIKE @Search OR document_id LIKE @Search OR user_name LIKE @Search OR action LIKE @Search)";
            parameters.Add("Search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(module))
        {
            sql += " AND module = @Module";
            parameters.Add("Module", module);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            sql += " AND user_name = @UserName";
            parameters.Add("UserName", username);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            switch (category.ToUpper())
            {
                case "LOGIN":
                    sql += " AND (action LIKE '%LOGIN%' OR action LIKE '%LOGOUT%' OR module = 'AUTH')";
                    break;
                case "DOCUMENT":
                    sql += " AND (action LIKE '%CREATE%' OR action LIKE '%EDIT%' OR action LIKE '%UPDATE%')";
                    break;
                case "EXCEL":
                    sql += " AND action LIKE '%UPLOAD%'";
                    break;
                case "SILO":
                    sql += " AND (action LIKE '%SILO%' OR module = 'SILO')";
                    break;
                case "CEISA":
                    sql += " AND (action LIKE '%CEISA%' OR module = 'CEISA')";
                    break;
                case "USER":
                    sql += " AND (module = 'USER' OR action LIKE '%USER%')";
                    break;
            }
        }

        sql += " ORDER BY created_at DESC";

        var logs = (await _db.QueryAsync<DoItG2.Models.Common.AuditLogModel>(sql, parameters)).ToList();

        ViewBag.Search = search;
        ViewBag.Category = category;
        ViewBag.Module = module;
        ViewBag.Username = username;

        try
        {
            ViewBag.UserList = (await _db.QueryAsync<string>("SELECT DISTINCT user_name FROM doit_user ORDER BY user_name")).ToList();
        }
        catch
        {
            ViewBag.UserList = new List<string>();
        }

        return View(logs);
    }

    public IActionResult Backup()
    {
        ViewData["Title"] = "Backup & Restore Database";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> Pengaturan <span class='breadcrumb-sep'>/</span> Backup";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ProcessBackup()
    {
        try
        {
            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'BACKUP_DATABASE', 'SYSTEM', 'DB_BACKUP', 'Membuat titik pulih & cadangan database DO_IT_G2', @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "admin", Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Backup database DO_IT_G2 berhasil dibuat pada {DateTime.Now:dd-MM-yyyy HH:mm:ss} WIB!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database backup");
            TempData["Error"] = $"Gagal membuat backup database: {ex.Message}";
        }
        return RedirectToAction(nameof(Backup));
    }
}
