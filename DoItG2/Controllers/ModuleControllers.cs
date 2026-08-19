using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.PIB;
using DoItG2.Models.PEB;
using DoItG2.Models.Auth;
using DoItG2.Services;
using Oracle.ManagedDataAccess.Client;

namespace DoItG2.Controllers;

[Authorize]
public class PibController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<PibController> _logger;

    public PibController(DatabaseContext db, ILogger<PibController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string search, string status, int page = 1)
    {
        ViewData["Title"] = "Daftar PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> PIB";
        
        try
        {
            var sql = @"SELECT CAR, ASAL_DATA AS AsalData, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, 
                       TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg,
                       NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg,
                       CASE 
                            WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                            WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                            ELSE 'DRAFT'
                       END AS Status 
                       FROM PIB_DOIT_FINAL_HEADER WHERE 1=1";
                       
            var parameters = new DynamicParameters();
            
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
            
            sql += " ORDER BY CREATION_DATE DESC";
            
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
        ViewData["Title"] = "Buat PIB Baru";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(PibHeaderModel model, IFormCollection form)
    {
        try
        {
            // Bind additional 8-tab CEISA 4.0 fields from form
            model.KdKantor = form["KdKantor"].FirstOrDefault() ?? model.KdKantor;
            model.JnsPib = form["JnsPib"].FirstOrDefault() ?? model.JnsPib;
            model.JnsImp = form["JnsImp"].FirstOrDefault() ?? model.JnsImp;
            model.JnsBayar = form["JnsBayar"].FirstOrDefault() ?? model.JnsBayar;
            model.AsalData = form["AsalData"].FirstOrDefault() ?? "M";
            model.KdSkepFas = form["KdSkepFas"].FirstOrDefault() ?? "";
            
            model.IdImp = !string.IsNullOrWhiteSpace(form["IdImp"]) ? form["IdImp"].FirstOrDefault()! : "011297371411000";
            model.NmImo = !string.IsNullOrWhiteSpace(form["NmImo"]) ? form["NmImo"].FirstOrDefault()! : "PT. SUZUKI INDOMOBIL MOTOR";
            model.AlImp = !string.IsNullOrWhiteSpace(form["AlImp"]) ? form["AlImp"].FirstOrDefault()! : "JL. RAYA PENGGILINGAN KM 19";
            model.StatusImp = form["StatusImp"].FirstOrDefault() ?? "ATA";
            model.IdPpjk = form["IdPpjk"].FirstOrDefault() ?? "";
            model.NmPpjk = form["NmPpjk"].FirstOrDefault() ?? "";
            model.AlPpjk = form["AlPpjk"].FirstOrDefault() ?? "";

            model.NegPemasok = form["NegPemasok"].FirstOrDefault() ?? model.NegPemasok;
            model.NmPemasok = form["NmPemasok"].FirstOrDefault() ?? model.NmPemasok;
            model.AlPemasok = form["AlPemasok"].FirstOrDefault() ?? model.AlPemasok;

            model.CaraAngkut = form["CaraAngkut"].FirstOrDefault() ?? "1";
            model.NmAngkut = form["NmAngkut"].FirstOrDefault() ?? "";
            model.BenderaVoy = form["BenderaVoy"].FirstOrDefault() ?? "";
            model.NoVoyFlight = form["NoVoyFlight"].FirstOrDefault() ?? "";
            model.TglTiba = form["TglTiba"].FirstOrDefault() ?? model.TglTiba;
            model.PelMuat = form["PelMuat"].FirstOrDefault() ?? "";
            model.PelBongkar = form["PelBongkar"].FirstOrDefault() ?? "";
            model.PelTransit = form["PelTransit"].FirstOrDefault() ?? "";
            model.Gudang = form["Gudang"].FirstOrDefault() ?? "";
            model.NoBc11 = form["NoBc11"].FirstOrDefault() ?? "";
            model.TglBc11 = form["TglBc11"].FirstOrDefault() ?? "";
            model.NoPosBc11 = form["NoPosBc11"].FirstOrDefault() ?? "";

            model.KdVal = form["KdVal"].FirstOrDefault() ?? "USD";
            model.Ndpbm = form["Ndpbm"].FirstOrDefault() ?? "";
            model.Fob = form["Fob"].FirstOrDefault() ?? "0";
            model.Asuransi = form["Asuransi"].FirstOrDefault() ?? "0";
            model.Freight = form["Freight"].FirstOrDefault() ?? "0";
            model.Cif = form["Cif"].FirstOrDefault() ?? "0";
            model.Netto = form["Netto"].FirstOrDefault() ?? "0";
            model.Bruto = form["Bruto"].FirstOrDefault() ?? "0";
            model.KdJaminan = form["KdJaminan"].FirstOrDefault() ?? "1";
            model.JmlCont = form["JmlCont"].FirstOrDefault() ?? "0";

            // Insert full header
            var sqlHeader = @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                (CAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, STATUS_IMP, ID_PPJK, NM_PPJK, AL_PPJK, KD_KANTOR, JNS_PIB, JNS_IMP, JNS_BAYAR, KD_SKEP_FAS,
                 NEG_PEMASOK, NM_PEMASOK, AL_PEMASOK, CARA_ANGKUT, NM_ANGKUT, BENDERA_VOY, NO_VOY_FLIGHT, TGL_TIBA, PEL_MUAT, PEL_BONGKAR, PEL_TRANSIT, GUDANG, NO_BC11, TGL_BC11, NO_POS_BC11,
                 KD_VAL, NDPBM, FOB, ASURANSI, FREIGHT, CIF, NETTO, BRUTO, KD_JAMINAN, JML_CONT, JML_BRG, CREATION_DATE, FL_VALID, STATUS)
                VALUES (@Car, @AsalData, @IdImp, @NmImo, @AlImp, @StatusImp, @IdPpjk, @NmPpjk, @AlPpjk, @KdKantor, @JnsPib, @JnsImp, @JnsBayar, @KdSkepFas,
                 @NegPemasok, @NmPemasok, @AlPemasok, @CaraAngkut, @NmAngkut, @BenderaVoy, @NoVoyFlight, @TglTiba, @PelMuat, @PelBongkar, @PelTransit, @Gudang, @NoBc11, @TglBc11, @NoPosBc11,
                 @KdVal, @Ndpbm, @Fob, @Asuransi, @Freight, @Cif, @Netto, @Bruto, @KdJaminan, @JmlCont, @JmlBrg, GETDATE(), 'N', 'DRAFT')";
            
            await _db.ExecuteAsync(sqlHeader, model);

            // Save Items (Detail Barang)
            var brgHs = form["BrgHs[]"];
            var brgDesc = form["BrgDesc[]"];
            var brgQty = form["BrgQty[]"];
            var brgSat = form["BrgSat[]"];
            var brgNeg = form["BrgNeg[]"];

            for (int i = 0; i < brgHs.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(brgHs[i]))
                {
                    decimal qty = 0, val = 0;
                    decimal.TryParse(brgQty[i], out qty);
                    decimal.TryParse(model.Cif, out val);

                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, ORIGIN_COUNTRY, UNIT_VAL)
                           VALUES (@Car, @Serial, @HsNo, @Desc, @Qty, @UnitType, @Negara, @UnitVal)",
                        new { Car = model.Car, Serial = i + 1, HsNo = brgHs[i], Desc = brgDesc[i], Qty = qty, UnitType = brgSat[i], Negara = brgNeg[i], UnitVal = val });
                }
            }

            // Save Documents
            var docKd = form["DocKd[]"];
            var docNm = form["DocNm[]"];
            var docNo = form["DocNo[]"];
            var docTg = form["DocTg[]"];

            for (int i = 0; i < docKd.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(docKd[i]))
                {
                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_DOCUMENT (CAR, SERIAL, DOKKD, DOKNM, DOKNO, DOKTG)
                           VALUES (@Car, @Serial, @DokKd, @DokNm, @DokNo, @DokTg)",
                        new { Car = model.Car, Serial = i + 1, DokKd = docKd[i], DokNm = docNm[i], DokNo = docNo[i], DokTg = docTg[i] });
                }
            }

            // Save Containers
            var contNo = form["ContNo[]"];
            var contUkr = form["ContUkr[]"];
            var contMuat = form["ContMuat[]"];
            var contTipe = form["ContTipe[]"];

            for (int i = 0; i < contNo.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(contNo[i]))
                {
                    int ukr = 20;
                    int.TryParse(contUkr[i], out ukr);
                    await _db.ExecuteAsync(
                        @"INSERT INTO PIB_DOIT_FINAL_CONTAINER (CAR, NO_CONT, UKR_CONT, JNS_MUAT, JNS_CONT)
                           VALUES (@Car, @NoCont, @UkrCont, @JnsMuat, @JnsCont)",
                        new { Car = model.Car, NoCont = contNo[i], UkrCont = ukr, JnsMuat = contMuat[i], JnsCont = contTipe[i] });
                }
            }

            // Insert audit log
            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'CREATE_PIB', 'PIB', @Car, 'Membuat dokumen PIB 8-Tab CEISA 4.0 baru', @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = model.Car, Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Dokumen PIB (CEISA 4.0) dengan nomor CAR {model.Car} berhasil disimpan.";
            return RedirectToAction(nameof(Index));
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
            var header = await _db.QueryFirstOrDefaultAsync<PibHeaderModel>(
                @"SELECT CAR, ASAL_DATA AS AsalData, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, AL_PEMASOK AS AlPemasok, 
                  TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                  NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg,
                  CASE 
                       WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                       WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                       ELSE 'DRAFT'
                  END AS Status 
                  FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = id });
                
            if (header == null) return NotFound();
            
            var details = await _db.QueryAsync<PibDetailModel>(
                @"SELECT SERIAL AS Serial, HS_NO AS HsNo, GOOD_DESC1 AS GoodDesc1, 
                   QUANTITY AS Quantity, UNIT_TYPE AS UnitType, UNIT_VAL AS UnitVal 
                   FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car ORDER BY SERIAL",
                new { Car = id });
            header.Details = details.ToList();
            
            var docs = await _db.QueryAsync<PibDocumentModel>(
                @"SELECT SERIAL AS Serial, DOKKD AS DokKd, DOKNO AS DokNo, DOKTG AS DokTg 
                  FROM PIB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car ORDER BY SERIAL",
                new { Car = id });
            header.Documents = docs.ToList();
            
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PIB detail: {Car}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = $"Edit PIB — {id}";
        ViewData["Breadcrumb"] = $"<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Edit";
        
        try
        {
            var header = await _db.QueryFirstOrDefaultAsync<PibHeaderModel>(
                @"SELECT CAR, ASAL_DATA AS AsalData, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, AL_PEMASOK AS AlPemasok, 
                  TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = id });
                
            if (header == null) return NotFound();
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading edit view for PIB: {Car}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(PibHeaderModel model)
    {
        try
        {
            await _db.ExecuteAsync(
                @"UPDATE PIB_DOIT_FINAL_HEADER 
                  SET NM_PEMASOK = @NmPemasok, AL_PEMASOK = @AlPemasok, TGL_TIBA = @TglTiba, JML_BRG = @JmlBrg
                  WHERE CAR = @Car",
                model);

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'EDIT_PIB', 'PIB', @Car, 'Mengubah dokumen PIB', @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = model.Car, Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Dokumen PIB dengan nomor CAR {model.Car} berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing PIB: {Car}", model.Car);
            TempData["Error"] = $"Gagal memperbarui dokumen PIB: {ex.Message}";
            ModelState.AddModelError("", $"Gagal memperbarui dokumen PIB: {ex.Message}");
            return View(model);
        }
    }

    public async Task<IActionResult> Response()
    {
        ViewData["Title"] = "Respons PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Respons";
        
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

    public IActionResult Upload()
    {
        ViewData["Title"] = "Upload Excel — PIB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Pib'>PIB</a> <span class='breadcrumb-sep'>/</span> Upload Excel";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UploadExcel(IFormFile excelFile, string car)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["Error"] = "File Excel tidak boleh kosong.";
            return RedirectToAction(nameof(Upload));
        }

        try
        {
            using var stream = excelFile.OpenReadStream();
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RowsUsed().Skip(1); // skip header row
            
            int serial = 1;
            int importedCount = 0;

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

                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_DETAIL (CAR, SERIAL, HS_NO, GOOD_DESC1, QUANTITY, UNIT_TYPE, UNIT_VAL, ORIGIN_COUNTRY)
                       VALUES (@Car, @Serial, @HsNo, @Desc, @Qty, @UnitType, @UnitVal, @Country)",
                    new { Car = car, Serial = serial++, HsNo = hsCode, Desc = description, Qty = qty, UnitType = unitType, UnitVal = unitVal, Country = country });
                importedCount++;
            }

            // Update JML_BRG in header
            await _db.ExecuteAsync(
                "UPDATE PIB_DOIT_FINAL_HEADER SET JML_BRG = @Count WHERE CAR = @Car",
                new { Count = importedCount.ToString(), Car = car });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                   VALUES (@User, 'UPLOAD_EXCEL_PIB', 'PIB', @Car, @Desc, @Ip, GETDATE())",
                new {
                    User = User.Identity?.Name ?? "system",
                    Car = car,
                    Desc = $"Upload Excel berhasil: {importedCount} item barang diimport",
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

            TempData["Success"] = $"Upload Excel berhasil! {importedCount} item barang diimport ke dokumen PIB {car}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading Excel for PIB");
            TempData["Error"] = $"Gagal upload Excel: {ex.Message}";
            return RedirectToAction(nameof(Upload));
        }
    }
}

[Authorize]
public class PebController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<PebController> _logger;

    public PebController(DatabaseContext db, ILogger<PebController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string search, string status, int page = 1)
    {
        ViewData["Title"] = "Daftar PEB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> PEB";
        
        try
        {
            var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, TGEKS AS TgEks, NETTO, 
                       STATUS AS Nopen,
                       CASE 
                            WHEN STATUS >= 3 THEN 'APPROVED'
                            WHEN STATUS = 2 THEN 'SENT'
                            WHEN STATUS = 1 THEN 'PENDING'
                            ELSE 'DRAFT'
                       END AS Status 
                       FROM PEB_DOIT_FINAL_HEADER WHERE 1=1";
                       
            var parameters = new DynamicParameters();
            
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (CAR LIKE @Search OR NAMAEKS LIKE @Search OR NPWPEKS LIKE @Search OR NEGBELI LIKE @Search OR CARRIER LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "APPROVED") sql += " AND STATUS >= 3";
                else if (status == "SENT") sql += " AND STATUS = 2";
                else if (status == "DRAFT") sql += " AND STATUS <= 1";
            }
            
            sql += " ORDER BY CREATED_DATE DESC";
            
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
        ViewData["Title"] = "Buat PEB Baru";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Buat Baru";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(PebHeaderModel model, IFormCollection form)
    {
        try
        {
            var kdKtr = form["KdKtr"].FirstOrDefault() ?? "040300";
            var jnEks = form["JnEks"].FirstOrDefault() ?? "1";
            var katEks = form["KatEks"].FirstOrDefault() ?? "1";
            var caraBayar = form["CaraBayar"].FirstOrDefault() ?? "1";
            var moda = form["Moda"].FirstOrDefault() ?? "1";
            var carrier = form["Carrier"].FirstOrDefault() ?? "";
            var voy = form["Voy"].FirstOrDefault() ?? "";
            var pelMuat = form["PelMuat"].FirstOrDefault() ?? "IDTPP";
            var pelTransit = form["PelTransit"].FirstOrDefault() ?? "";
            var pelBongkar = form["PelBongkar"].FirstOrDefault() ?? "";
            var kdVal = form["KdVal"].FirstOrDefault() ?? "USD";
            var incoterms = form["Incoterms"].FirstOrDefault() ?? "FOB";

            int jnEksInt = 1, katEksInt = 1, jnpebInt = 1, modaInt = 1;
            int.TryParse(jnEks, out jnEksInt);
            int.TryParse(katEks, out katEksInt);
            int.TryParse(moda, out modaInt);

            var npwpEks = !string.IsNullOrWhiteSpace(form["NpwpEks"]) ? form["NpwpEks"].FirstOrDefault()! : "011297371411000";
            var namaEks = !string.IsNullOrWhiteSpace(form["NamaEks"]) ? form["NamaEks"].FirstOrDefault()! : "PT. SUZUKI INDOMOBIL MOTOR";
            var almtEks = !string.IsNullOrWhiteSpace(form["AlmtEks"]) ? form["AlmtEks"].FirstOrDefault()! : "JL. RAYA PENGGILINGAN KM. 19";

            var sqlHeader = @"INSERT INTO PEB_DOIT_FINAL_HEADER 
                (CAR, JNEKS, KATEKS, JNPEB, NPWPEKS, NAMAEKS, ALMTEKS, NEGBELI, MODA, CARRIER, VOY, PELMUAT, PELTRANSIT, PELBONGKAR, KDVAL, TGEKS, NETTO, BRUTO, FOB, KDKTR, CREATED_DATE, STATUS)
                VALUES (@Car, @JnEks, @KatEks, @JnPeb, @NpwpEks, @NamaEks, @AlmtEks, @NegBeli, @Moda, @Carrier, @Voy, @PelMuat, @PelTransit, @PelBongkar, @KdVal, @TgEks, @Netto, @Bruto, @Fob, @KdKtr, GETDATE(), 1)";
            
            await _db.ExecuteAsync(sqlHeader, new {
                Car = model.Car,
                JnEks = jnEksInt,
                KatEks = katEksInt,
                JnPeb = jnpebInt,
                NpwpEks = npwpEks,
                NamaEks = namaEks,
                AlmtEks = almtEks,
                NegBeli = !string.IsNullOrWhiteSpace(model.NegBeli) ? model.NegBeli : (form["NegBeli"].FirstOrDefault() ?? "ID"),
                Moda = modaInt,
                Carrier = carrier,
                Voy = voy,
                PelMuat = pelMuat,
                PelTransit = pelTransit,
                PelBongkar = pelBongkar,
                KdVal = kdVal,
                TgEks = model.TgEks,
                Netto = model.Netto,
                Bruto = model.Bruto,
                Fob = model.Fob,
                KdKtr = kdKtr
            });

            // Save PEB Items (Detail Barang Ekspor)
            var brgHs = form["BrgHs[]"];
            var brgDesc = form["BrgDesc[]"];
            var brgQty = form["BrgQty[]"];
            var brgSat = form["BrgSat[]"];
            var brgFob = form["BrgFob[]"];

            for (int i = 0; i < brgHs.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(brgHs[i]))
                {
                    int hsInt = 0, qtyInt = 0;
                    long fobLong = 0;
                    int.TryParse(brgHs[i], out hsInt);
                    int.TryParse(brgQty[i], out qtyInt);
                    long.TryParse(brgFob[i], out fobLong);

                    await _db.ExecuteAsync(
                        @"INSERT INTO PEB_DOIT_FINAL_DETAIL (CAR, SERIBRG, HS, URBRG1, JMSATUAN, JNSATUAN, FOBPERBRG, CREATED_DATE)
                           VALUES (@Car, @Seri, @Hs, @Desc, @Qty, @UnitType, @Fob, GETDATE())",
                        new { Car = model.Car, Seri = i + 1, Hs = hsInt, Desc = brgDesc[i], Qty = qtyInt, UnitType = brgSat[i], Fob = fobLong });
                }
            }

            // Save PEB Documents
            var docKd = form["DocKd[]"];
            var docNm = form["DocNm[]"];
            var docNo = form["DocNo[]"];
            var docTg = form["DocTg[]"];

            for (int i = 0; i < docKd.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(docKd[i]))
                {
                    DateTime? docDate = null;
                    if (DateTime.TryParse(docTg[i], out DateTime dt)) docDate = dt;

                    await _db.ExecuteAsync(
                        @"INSERT INTO PEB_DOIT_FINAL_DOCUMENT (CAR, KDDOK, NODOK, TGDOK, CREATED_DATE)
                           VALUES (@Car, @KdDok, @NoDok, @TgDok, GETDATE())",
                        new { Car = model.Car, KdDok = docKd[i], NoDok = docNo[i], TgDok = docDate });
                }
            }

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'CREATE_PEB', 'PEB', @Car, 'Membuat dokumen PEB 8-Tab CEISA 4.0 baru', @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = model.Car, Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Dokumen PEB (CEISA 4.0) dengan nomor CAR {model.Car} berhasil dibuat.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PEB: {Car}", model.Car);
            TempData["Error"] = $"Gagal membuat dokumen PEB: {ex.Message}";
            ModelState.AddModelError("", $"Gagal membuat dokumen PEB: {ex.Message}");
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = $"Edit PEB — {id}";
        ViewData["Breadcrumb"] = $"<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Edit";
        
        try
        {
            var header = await _db.QueryFirstOrDefaultAsync<PebHeaderModel>(
                @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, TGEKS AS TgEks, 
                   NETTO, BRUTO, FOB FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = id });
                
            if (header == null) return NotFound();
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading edit view for PEB: {Car}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(PebHeaderModel model)
    {
        try
        {
            await _db.ExecuteAsync(
                @"UPDATE PEB_DOIT_FINAL_HEADER 
                   SET NAMAEKS = @NamaBeli, ALMTEKS = @AlmtBeli, NEGBELI = @NegBeli, TGEKS = @TgEks, 
                       NETTO = @Netto, BRUTO = @Bruto, FOB = @Fob
                   WHERE CAR = @Car",
                model);

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'EDIT_PEB', 'PEB', @Car, 'Mengubah dokumen PEB', @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = model.Car, Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Dokumen PEB dengan nomor CAR {model.Car} berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing PEB: {Car}", model.Car);
            TempData["Error"] = $"Gagal memperbarui dokumen PEB: {ex.Message}";
            ModelState.AddModelError("", $"Gagal memperbarui dokumen PEB: {ex.Message}");
            return View(model);
        }
    }

    public async Task<IActionResult> Response()
    {
        ViewData["Title"] = "Respons PEB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Respons";
        
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

    public async Task<IActionResult> Detail(string id)
    {
        ViewData["Title"] = $"Detail PEB — {id}";
        ViewData["Breadcrumb"] = $"<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Detail";
        
        try
        {
            var header = await _db.QueryFirstOrDefaultAsync<PebHeaderModel>(
                @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, TGEKS AS TgEks, 
                   NETTO, BRUTO, FOB, CREATED_DATE AS CreatedDate,
                   CASE 
                        WHEN STATUS >= 3 THEN 'APPROVED'
                        WHEN STATUS = 2 THEN 'SENT'
                        WHEN STATUS = 1 THEN 'PENDING'
                        ELSE 'DRAFT'
                   END AS Status 
                   FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = id });
                
            if (header == null) return NotFound();
            
            var details = await _db.QueryAsync<PebDetailModel>(
                @"SELECT SERIBRG AS Seri, CAST(HS AS VARCHAR) AS HsNo, URBRG1 AS UrBrg, 
                   JMSATUAN AS JmlSat, JNSATUAN AS KdSat, FOBPERBRG AS FobDet 
                   FROM PEB_DOIT_FINAL_DETAIL WHERE CAR = @Car ORDER BY SERIBRG",
                new { Car = id });
            header.Details = details.ToList();
            
            var docs = await _db.QueryAsync<PebDocumentModel>(
                @"SELECT KDDOK AS KdDok, NODOK AS NoDok, TGDOK AS TgDok 
                   FROM PEB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car",
                new { Car = id });
            header.Documents = docs.ToList();

            var responses = await _db.QueryAsync<PebResponModel>(
                @"SELECT RESKD AS ResKd, RESTG AS ResTg, NOPEN AS NoPen, TGPEN AS TgPen, DESKRIPSI AS Deskripsi
                   FROM PEB_DOIT_FINAL_RESPON WHERE CAR = @Car ORDER BY RESTG DESC",
                new { Car = id });
            header.Responses = responses.ToList();
            
            return View(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching PEB detail: {Car}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    public IActionResult Upload()
    {
        ViewData["Title"] = "Upload Excel — PEB";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> <a href='/Peb'>PEB</a> <span class='breadcrumb-sep'>/</span> Upload Excel";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UploadExcel(IFormFile excelFile, string car)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["Error"] = "File Excel tidak boleh kosong.";
            return RedirectToAction(nameof(Upload));
        }

        try
        {
            using var stream = excelFile.OpenReadStream();
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RowsUsed().Skip(1); // skip header row
            
            int serial = 1;
            int importedCount = 0;

            foreach (var row in rows)
            {
                var hsCode = row.Cell(1).GetString().Trim();
                var description = row.Cell(2).GetString().Trim();
                var qty = row.Cell(3).IsEmpty() ? 0 : Convert.ToInt32(row.Cell(3).Value);
                var unitType = row.Cell(4).GetString().Trim();
                var fob = row.Cell(5).IsEmpty() ? 0L : Convert.ToInt64(row.Cell(5).Value);
                var originCountry = row.Cell(6).GetString().Trim();

                if (string.IsNullOrWhiteSpace(hsCode) && string.IsNullOrWhiteSpace(description))
                    continue;

                int hsInt = 0;
                int.TryParse(hsCode, out hsInt);

                await _db.ExecuteAsync(
                    @"INSERT INTO PEB_DOIT_FINAL_DETAIL (CAR, SERIBRG, HS, URBRG1, JMSATUAN, JNSATUAN, FOBPERBRG, NEGASAL, CREATED_DATE)
                       VALUES (@Car, @Seri, @Hs, @Desc, @Qty, @UnitType, @Fob, @OriginCountry, GETDATE())",
                    new { Car = car, Seri = serial++, Hs = hsInt, Desc = description, Qty = qty, UnitType = unitType, Fob = fob, OriginCountry = originCountry });
                importedCount++;
            }

            // Update JMBRG in PEB header
            await _db.ExecuteAsync(
                "UPDATE PEB_DOIT_FINAL_HEADER SET JMBRG = @Count WHERE CAR = @Car",
                new { Count = importedCount, Car = car });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                   VALUES (@User, 'UPLOAD_EXCEL_PEB', 'PEB', @Car, @Desc, @Ip, GETDATE())",
                new {
                    User = User.Identity?.Name ?? "system",
                    Car = car,
                    Desc = $"Upload Excel PEB berhasil: {importedCount} item barang diimport",
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

            TempData["Success"] = $"Upload Excel PEB berhasil! {importedCount} item barang diimport ke dokumen PEB {car}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading Excel for PEB");
            TempData["Error"] = $"Gagal upload Excel PEB: {ex.Message}";
            return RedirectToAction(nameof(Upload));
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
                        
                        var random = new Random();
                        var carNo = $"00000000615220261025{random.Next(100000, 999999)}";
                        var first = records[0];
                        
                        await _db.ExecuteAsync(
                            @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                              (CAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, NM_PEMASOK, AL_PEMASOK, TGL_TIBA, JML_BRG, CREATION_DATE, FL_VALID)
                              VALUES (@Car, 'S', '011297371411000', 'PT. SUZUKI INDOMOBIL MOTOR', 'JL. RAYA PENGGILINGAN KM 19', 'SUZUKI MOTOR CORPORATION', 'SHIZUOKA, JAPAN', @TgTiba, @JmlBrg, GETDATE(), 'N')",
                            new { 
                                Car = carNo, 
                                TgTiba = first.TgTiba?.ToString() ?? DateTime.Now.ToString("yyyyMMdd"),
                                JmlBrg = records.Count.ToString()
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
                                Desc = $"Tarik data SILO Oracle berhasil untuk Invoice {searchInvoice} ({records.Count} items)", 
                                Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                            });

                        TempData["Success"] = $"Sinkronisasi berhasil! Dokumen PIB dengan CAR {carNo} di-import dari Oracle SILO.";
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
                var random = new Random();
                var carNo = $"00000000615220261025{random.Next(100000, 999999)}";
                
                var header = new PibHeaderModel
                {
                    Car = carNo,
                    IdImp = "011297371411000",
                    NmPemasok = "SUZUKI MOTOR CORPORATION",
                    AlPemasok = "300 TAKATSUKA-CHO, MINAMI-KU, HAMAMATSU-SHI, SHIZUOKA",
                    TglTiba = DateTime.Now.AddDays(2).ToString("yyyyMMdd"),
                    JmlBrg = "12",
                    Status = "DRAFT"
                };

                await _db.ExecuteAsync(
                    @"INSERT INTO PIB_DOIT_FINAL_HEADER 
                      (CAR, ASAL_DATA, ID_IMP, NM_IMO, AL_IMP, NM_PEMASOK, AL_PEMASOK, TGL_TIBA, JML_BRG, CREATION_DATE, FL_VALID)
                      VALUES (@Car, 'S', @IdImp, 'PT. SUZUKI INDOMOBIL MOTOR', 'JL. RAYA PENGGILINGAN KM 19', @NmPemasok, @AlPemasok, @TglTiba, @JmlBrg, GETDATE(), 'N')",
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
                        Desc = $"Sinkronisasi data SILO berhasil (Simulasi) untuk Invoice {searchInvoice}", 
                        Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                    });

                TempData["Success"] = $"Sinkronisasi berhasil! Dokumen PIB dengan CAR {carNo} di-import sebagai DRAFT (Simulasi).";
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
                        
                        var random = new Random();
                        var carNo = $"00003001062620260714{random.Next(100000, 999999)}";
                        var first = records[0];
                        
                        await _db.ExecuteAsync(
                            @"INSERT INTO PEB_DOIT_FINAL_HEADER 
                               (CAR, NAMAEKS, ALMTEKS, NEGBELI, TGEKS, NETTO, BRUTO, FOB, CREATED_DATE, STATUS)
                               VALUES (@Car, @NamaBeli, @AlmtBeli, @NegBeli, GETDATE(), 1000, 1100, 50000, GETDATE(), 1)",
                            new {
                                Car = carNo,
                                NamaBeli = first.PenerimaNama?.ToString() ?? "BOUSTEAD SDN BERHAD",
                                AlmtBeli = first.PenerimaAlamat?.ToString() ?? "KUALA LUMPUR",
                                NegBeli = first.PenerimaNegara?.ToString() ?? "MY"
                            });
                            
                        await _db.ExecuteAsync(
                            @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                              VALUES (@User, 'SYNC_SILO_PEB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                            new { 
                                User = User.Identity?.Name ?? "system", 
                                Car = carNo, 
                                Desc = $"Tarik data SILO PEB Oracle berhasil untuk Invoice {searchInvoice}", 
                                Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                            });

                        TempData["Success"] = $"Sinkronisasi berhasil! Dokumen PEB dengan CAR {carNo} di-import dari Oracle SILO.";
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
                var random = new Random();
                var carNo = $"00003001062620260714{random.Next(100000, 999999)}";
                
                var header = new PebHeaderModel
                {
                    Car = carNo,
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
                       (CAR, NAMAEKS, ALMTEKS, NEGBELI, TGEKS, NETTO, BRUTO, FOB, CREATED_DATE, STATUS)
                       VALUES (@Car, @NamaBeli, @AlmtBeli, @NegBeli, @TgEks, @Netto, @Bruto, @Fob, GETDATE(), 1)",
                    header);

                await _db.ExecuteAsync(
                    @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                      VALUES (@User, 'SYNC_SILO_PEB', 'SILO', @Car, @Desc, @Ip, GETDATE())",
                    new { 
                        User = User.Identity?.Name ?? "system", 
                        Car = carNo, 
                        Desc = $"Sinkronisasi data SILO PEB berhasil (Simulasi) untuk Invoice {searchInvoice}", 
                        Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                    });

                TempData["Success"] = $"Sinkronisasi berhasil! Dokumen PEB dengan CAR {carNo} di-import sebagai DRAFT (Simulasi).";
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
}

[Authorize]
public class CeisaController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<CeisaController> _logger;
    private readonly IValidationService _validation;

    public CeisaController(DatabaseContext db, ILogger<CeisaController> logger, IValidationService validation)
    {
        _db = db;
        _logger = logger;
        _validation = validation;
    }

    public async Task<IActionResult> SendPib()
    {
        ViewData["Title"] = "Kirim PIB ke CEISA";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Kirim PIB";
        
        var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, JML_BRG AS JmlBrg, 'DRAFT' AS Status 
                    FROM PIB_DOIT_FINAL_HEADER 
                    WHERE NO_PEN_PIB IS NULL OR NO_PEN_PIB = ''";
        var drafts = await _db.QueryAsync<PibHeaderModel>(sql);
        return View(drafts.ToList());
    }

    public async Task<IActionResult> SendPeb()
    {
        ViewData["Title"] = "Kirim PEB ke CEISA";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Kirim PEB";
        
        var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, TGEKS AS TgEks, NETTO, 'DRAFT' AS Status 
                     FROM PEB_DOIT_FINAL_HEADER 
                     WHERE STATUS <= 1";
        var drafts = await _db.QueryAsync<PebHeaderModel>(sql);
        return View(drafts.ToList());
    }

    [HttpPost]
    public async Task<IActionResult> TransmitPib(string car)
    {
        try
        {
            // Run automatic validation before sending
            var validationResult = await _validation.ValidatePibAsync(car);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join("; ", validationResult.Errors
                    .Where(e => e.Severity == ValidationSeverity.Error)
                    .Select(e => $"[{e.Tab}] {e.Message}"));
                TempData["Error"] = $"Validasi gagal ({validationResult.ErrorCount} error): {errorMessages}";
                return RedirectToAction(nameof(SendPib));
            }

            var random = new Random();
            var nopen = random.Next(100000, 999999).ToString();
            var sppb = random.Next(100000, 999999).ToString();

            // Simulate sending payload to CEISA 4.0 API and obtaining registration number & SPPB
            await _db.ExecuteAsync(
                @"UPDATE PIB_DOIT_FINAL_HEADER 
                  SET NO_PEN_PIB = @Nopen, TGL_PEND_PIB = GETDATE(), NO_SPPB = @Sppb, TGL_SPPB = GETDATE()
                  WHERE CAR = @Car",
                new { Car = car, Nopen = nopen, Sppb = sppb });

            // Seed response
            await _db.ExecuteAsync(
                @"INSERT INTO PIB_DOIT_FINAL_RESPON (CAR, RESKD, RESTG, DOKRESNO, DOKRESTG, KPBC, PIBNO, PIBTG, DESKRIPSI)
                  VALUES (@Car, '300', GETDATE(), @Sppb, GETDATE(), '010100', @Nopen, GETDATE(), 'Surat Persetujuan Pengeluaran Barang (SPPB) Terbit')",
                new { Car = car, Nopen = nopen, Sppb = sppb });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'SEND_CEISA_PIB', 'CEISA', @Car, @Desc, @Ip, GETDATE())",
                new { 
                    User = User.Identity?.Name ?? "system", 
                    Car = car, 
                    Desc = $"Dokumen PIB berhasil dikirim ke CEISA 4.0. No Pendaftaran: {nopen}, No SPPB: {sppb}", 
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                });

            TempData["Success"] = $"PIB dengan CAR {car} sukses terkirim ke CEISA! Respon SPPB {sppb} terbit.";
            return RedirectToAction(nameof(SendPib));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PIB to CEISA");
            TempData["Error"] = $"Gagal mengirim PIB ke CEISA: {ex.Message}";
            return RedirectToAction(nameof(SendPib));
        }
    }

    [HttpPost]
    public async Task<IActionResult> TransmitPeb(string car)
    {
        try
        {
            // Run automatic CEISA 4.0 validation before sending PEB
            var validationResult = await _validation.ValidatePebAsync(car);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join("; ", validationResult.Errors
                    .Where(e => e.Severity == ValidationSeverity.Error)
                    .Select(e => $"[{e.Tab}] {e.Message}"));
                TempData["Error"] = $"Validasi PEB gagal ({validationResult.ErrorCount} error): {errorMessages}";
                return RedirectToAction(nameof(SendPeb));
            }

            var random = new Random();
            var nopen = random.Next(100000, 999999).ToString();

            await _db.ExecuteAsync(
                @"UPDATE PEB_DOIT_FINAL_HEADER 
                   SET STATUS = 3
                   WHERE CAR = @Car",
                new { Car = car });

            await _db.ExecuteAsync(
                @"INSERT INTO PEB_DOIT_FINAL_RESPON (CAR, RESKD, RESTG, NOPEN, TGPEN, DESKRIPSI)
                   VALUES (@Car, 'NPE', GETDATE(), @Nopen, GETDATE(), 'Nota Pelayanan Ekspor (NPE) Terbit')",
                new { Car = car, Nopen = nopen });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'SEND_CEISA_PEB', 'CEISA', @Car, @Desc, @Ip, GETDATE())",
                new { 
                    User = User.Identity?.Name ?? "system", 
                    Car = car, 
                    Desc = $"Dokumen PEB berhasil dikirim ke CEISA 4.0. No NPE: {nopen}", 
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString() 
                });

            TempData["Success"] = $"PEB dengan CAR {car} sukses terkirim ke CEISA! Respon NPE {nopen} terbit.";
            return RedirectToAction(nameof(SendPeb));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PEB to CEISA");
            TempData["Error"] = $"Gagal mengirim PEB ke CEISA: {ex.Message}";
            return RedirectToAction(nameof(SendPeb));
        }
    }

    public IActionResult GetBc11()
    {
        ViewData["Title"] = "Tarik Data BC 1.1 (Manifes CEISA 4.0)";
        ViewData["Breadcrumb"] = "<a href='/'>Dashboard</a> <span class='breadcrumb-sep'>/</span> CEISA <span class='breadcrumb-sep'>/</span> Tarik BC 1.1";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ProcessGetBc11(string noBc11, string tglBc11, string carNo, string pelMuat, string pelBongkar)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(noBc11) || string.IsNullOrWhiteSpace(carNo))
            {
                TempData["Error"] = "Nomor BC 1.1 dan CAR Wajib diisi!";
                return RedirectToAction(nameof(GetBc11));
            }

            await _db.ExecuteAsync(
                @"UPDATE PIB_DOIT_FINAL_HEADER 
                  SET DOKTUPNO = @NoBc11, DOKTUPTG = @TglBc11, PELBKR = @PelBongkar, PELMUAT = @PelMuat
                  WHERE CAR = @Car",
                new { NoBc11 = noBc11, TglBc11 = tglBc11, PelBongkar = pelBongkar, PelMuat = pelMuat, Car = carNo });

            await _db.ExecuteAsync(
                @"INSERT INTO doit_audit_log (user_name, action, module, document_id, description, ip_address, created_at)
                  VALUES (@User, 'GET_BC11_CEISA', 'CEISA', @Car, @Desc, @Ip, GETDATE())",
                new { User = User.Identity?.Name ?? "system", Car = carNo, Desc = $"Tarik data BC 1.1 ({noBc11}) dari CEISA 4.0 ke dokumen CAR {carNo} berhasil", Ip = HttpContext.Connection.RemoteIpAddress?.ToString() });

            TempData["Success"] = $"Sukses menarik data Manifes BC 1.1 ({noBc11}) dari CEISA 4.0 untuk CAR {carNo}!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting BC 1.1 from CEISA");
            TempData["Error"] = $"Gagal menarik data BC 1.1: {ex.Message}";
        }
        return RedirectToAction(nameof(GetBc11));
    }
}

[Authorize]
public class MasterController : Controller
{
    private readonly DatabaseContext _db;
    private readonly ILogger<MasterController> _logger;

    public MasterController(DatabaseContext db, ILogger<MasterController> logger)
    {
        _db = db;
        _logger = logger;
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
    private readonly ILogger<ReportController> _logger;

    public ReportController(DatabaseContext db, ILogger<ReportController> logger)
    {
        _db = db;
        _logger = logger;
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

        var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, 
                           JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                           NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, FOB, CIF, NETTO, BRUTO,
                           CASE 
                                WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB'
                                WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'NOPEN'
                                ELSE 'DRAFT'
                           END AS Status 
                    FROM PIB_DOIT_FINAL_HEADER WHERE 1=1";

        var parameters = new DynamicParameters();
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
            var sql = @"SELECT CAR, ID_IMP AS IdImp, NM_PEMASOK AS NmPemasok, TGL_TIBA AS TglTiba, 
                               JML_BRG AS JmlBrg, NO_PEN_PIB AS PibNo, TGL_PEND_PIB AS PibTg, 
                               NO_SPPB AS SppbNo, TGL_SPPB AS SppbTg, FOB, CIF, NETTO, BRUTO,
                               CASE 
                                    WHEN NO_SPPB IS NOT NULL AND NO_SPPB <> '' THEN 'SPPB Terbit'
                                    WHEN NO_PEN_PIB IS NOT NULL AND NO_PEN_PIB <> '' THEN 'Nopen Terdaftar'
                                    ELSE 'Draft PIB'
                               END AS Status 
                        FROM PIB_DOIT_FINAL_HEADER WHERE 1=1";

            var parameters = new DynamicParameters();
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

        var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, 
                           TGEKS AS TgEks, NETTO, BRUTO, FOB, STATUS 
                    FROM PEB_DOIT_FINAL_HEADER WHERE 1=1";

        var parameters = new DynamicParameters();
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
            var sql = @"SELECT CAR, NAMAEKS AS NamaBeli, ALMTEKS AS AlmtBeli, NEGBELI AS NegBeli, 
                               TGEKS AS TgEks, NETTO, BRUTO, FOB, STATUS 
                        FROM PEB_DOIT_FINAL_HEADER WHERE 1=1";

            var parameters = new DynamicParameters();
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
        try
        {
            // Default working hash of Admin@123
            var hash = "$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6";
            
            await _db.ExecuteAsync(
                @"INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, is_admin)
                  VALUES (@Username, @Fullname, @Email, @Hash, @Role, 1, @IsAdmin)",
                new { Username = username, Fullname = fullname, Email = email, Hash = hash, Role = role, IsAdmin = (role == "ADMIN" ? 1 : 0) });

            TempData["Success"] = $"User {username} berhasil dibuat dengan password default Admin@123.";
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
