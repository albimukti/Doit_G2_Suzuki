using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.CEISA;
using DoItG2.Models.PIB;
using DoItG2.Models.PEB;
using DoItG2.Models.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DoItG2.Services;

public interface ICeisaIntegrationService
{
    Task<CeisaTransmitResult> TransmitPibAsync(string car, string username, bool isSandbox = true);
    Task<CeisaTransmitResult> TransmitPebAsync(string car, string username, bool isSandbox = true);
    Task<CeisaBc11ManifestData> PullBc11ManifestAsync(CeisaBc11PullRequest req, string username);
    Task<CeisaStatusTrackingResponse> GetTrackingTimelineAsync(string car, string docType);
    Task<string> GeneratePibPayloadJsonAsync(string car);
    Task<string> GeneratePebPayloadJsonAsync(string car);
}

public class CeisaIntegrationService : ICeisaIntegrationService
{
    private readonly DatabaseContext _db;
    private readonly IValidationService _validation;
    private readonly IAuditService _audit;
    private readonly IEmailNotificationService _notification;
    private readonly IConfiguration _config;
    private readonly ILogger<CeisaIntegrationService> _logger;

    public CeisaIntegrationService(
        DatabaseContext db,
        IValidationService validation,
        IAuditService audit,
        IEmailNotificationService notification,
        IConfiguration config,
        ILogger<CeisaIntegrationService> logger)
    {
        _db = db;
        _validation = validation;
        _audit = audit;
        _notification = notification;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GeneratePibPayloadJsonAsync(string car)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
        var details = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car", new { Car = car });
        var docs = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car", new { Car = car });
        var containers = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_CONTAINER WHERE CAR = @Car", new { Car = car });

        var payload = new
        {
            nomorAju = car,
            kodeDokumen = "20", // BC 2.0
            header = header,
            barang = details,
            dokumen = docs,
            kontainer = containers,
            waktuKirim = DateTime.UtcNow.ToString("o")
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> GeneratePebPayloadJsonAsync(string car)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
        var details = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_DETAIL WHERE CAR = @Car", new { Car = car });
        var docs = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car", new { Car = car });
        var containers = await _db.QueryAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_CONTAINER WHERE CAR = @Car", new { Car = car });

        var payload = new
        {
            nomorAju = car,
            kodeDokumen = "30", // BC 3.0
            header = header,
            barang = details,
            dokumen = docs,
            kontainer = containers,
            waktuKirim = DateTime.UtcNow.ToString("o")
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<CeisaTransmitResult> TransmitPibAsync(string car, string username, bool isSandbox = true)
    {
        var result = new CeisaTransmitResult { Car = car };

        try
        {
            // 1. Validation check
            var validation = await _validation.ValidatePibAsync(car);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.Message = $"Validasi gagal: {string.Join("; ", validation.Errors.Where(e => e.Severity == ValidationSeverity.Error).Select(e => e.Message))}";
                return result;
            }

            // 2. Generate CEISA 4.0 payload
            var rawJson = await GeneratePibPayloadJsonAsync(car);
            result.RawResponseJson = rawJson;

            // 3. Realistic CEISA Simulation
            var random = new Random();
            var nopen = random.Next(100000, 999999).ToString();
            var sppb = random.Next(100000, 999999).ToString();
            var billing = "82026" + random.Next(100000000, 999999999).ToString();
            var channels = new[] { "HIJAU", "HIJAU", "HIJAU", "MITA", "KUNING" };
            var channel = channels[random.Next(channels.Length)];

            result.Success = true;
            result.Nopen = nopen;
            result.TglNopen = DateTime.Now;
            result.NoSppb = sppb;
            result.TglSppb = DateTime.Now;
            result.BillingCode = billing;
            result.BillingExpiry = DateTime.Now.AddDays(3);
            result.Channel = channel;
            result.ResponseCode = "300";
            result.Message = $"Dokumen PIB berhasil diterima CEISA 4.0. No. Pendaftaran: {nopen}, Jalur: {channel}, SPPB: {sppb}";

            // Update Database
            await _db.ExecuteAsync(
                @"UPDATE PIB_DOIT_FINAL_HEADER 
                  SET NO_PEN_PIB = @Nopen, TGL_PEND_PIB = GETDATE(), 
                      NO_SPPB = @Sppb, TGL_SPPB = GETDATE(),
                      APPROVAL_STATUS = 'TRANSMITTED', STATUS = 'SPPB'
                  WHERE CAR = @Car",
                new { Nopen = nopen, Sppb = sppb, Car = car });

            // Record CEISA responses
            await _db.ExecuteAsync(
                @"INSERT INTO PIB_DOIT_FINAL_RESPON (CAR, RESKD, RESTG, DOKRESNO, DOKRESTG, KPBC, PIBNO, PIBTG, DESKRIPSI)
                  VALUES (@Car, '200', GETDATE(), @Billing, GETDATE(), '010100', @Nopen, GETDATE(), @BillingDesc);
                  
                  INSERT INTO PIB_DOIT_FINAL_RESPON (CAR, RESKD, RESTG, DOKRESNO, DOKRESTG, KPBC, PIBNO, PIBTG, DESKRIPSI)
                  VALUES (@Car, '300', GETDATE(), @Sppb, GETDATE(), '010100', @Nopen, GETDATE(), @SppbDesc);",
                new
                {
                    Car = car,
                    Billing = billing,
                    BillingDesc = $"Penerbitan Kode Billing Simponi {billing}. Jalur Pelayanan: {channel}",
                    Nopen = nopen,
                    Sppb = sppb,
                    SppbDesc = $"Surat Persetujuan Pengeluaran Barang (SPPB) Terbit No. {sppb}"
                });

            // Approval Log & Audit Log
            await _db.ExecuteAsync(
                @"INSERT INTO DOIT_APPROVAL_LOG (CAR, DOKUMEN_TYPE, PREV_STATUS, NEW_STATUS, ACTION, NOTES, ACTION_BY, ACTION_DATE)
                  VALUES (@Car, 'PIB', 'APPROVED', 'TRANSMITTED', 'TRANSMIT', @Notes, @User, GETDATE())",
                new { Car = car, Notes = $"Kirim ke CEISA 4.0. Respon Nopen: {nopen}, SPPB: {sppb}", User = username });

            await _audit.LogAsync(username, "TRANSMIT_CEISA_PIB", "CEISA", car, $"Dokumen PIB berhasil ditransmisikan ke CEISA 4.0. Nopen: {nopen}, SPPB: {sppb}");
            await _notification.NotifyDocumentStatusChangeAsync("PIB", car, $"SPPB TERBIT ({sppb})", $"Dokumen lolos validasi CEISA 4.0 Jalur {channel}. Nopen: {nopen}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PIB {Car} to CEISA", car);
            result.Success = false;
            result.Message = $"Gagal transmisi: {ex.Message}";
            return result;
        }
    }

    public async Task<CeisaTransmitResult> TransmitPebAsync(string car, string username, bool isSandbox = true)
    {
        var result = new CeisaTransmitResult { Car = car };

        try
        {
            // 1. Validation check
            var validation = await _validation.ValidatePebAsync(car);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.Message = $"Validasi gagal: {string.Join("; ", validation.Errors.Where(e => e.Severity == ValidationSeverity.Error).Select(e => e.Message))}";
                return result;
            }

            // 2. Realistic CEISA Simulation
            var random = new Random();
            var nopen = random.Next(100000, 999999).ToString();
            var npe = random.Next(100000, 999999).ToString();

            result.Success = true;
            result.Nopen = nopen;
            result.TglNopen = DateTime.Now;
            result.NoNpe = npe;
            result.TglNpe = DateTime.Now;
            result.Channel = "HIJAU";
            result.ResponseCode = "NPE";
            result.Message = $"Dokumen PEB berhasil diterima CEISA 4.0. No. Pendaftaran: {nopen}, NPE: {npe}";

            // Update Database
            await _db.ExecuteAsync(
                @"UPDATE PEB_DOIT_FINAL_HEADER 
                  SET STATUS = 3, APPROVAL_STATUS = 'TRANSMITTED', NOPEN = @Nopen, TGL_NOPEN = GETDATE()
                  WHERE CAR = @Car",
                new { Nopen = nopen, Car = car });

            // Record response
            await _db.ExecuteAsync(
                @"INSERT INTO PEB_DOIT_FINAL_RESPON (CAR, RESKD, RESTG, NOPEN, TGPEN, DESKRIPSI)
                  VALUES (@Car, 'NPE', GETDATE(), @Npe, GETDATE(), @Desc)",
                new { Car = car, Npe = npe, Desc = $"Nota Pelayanan Ekspor (NPE) Terbit No. {npe}. Siap muat ke kapal/pesawat." });

            // Approval Log & Audit Log
            await _db.ExecuteAsync(
                @"INSERT INTO DOIT_APPROVAL_LOG (CAR, DOKUMEN_TYPE, PREV_STATUS, NEW_STATUS, ACTION, NOTES, ACTION_BY, ACTION_DATE)
                  VALUES (@Car, 'PEB', 'APPROVED', 'TRANSMITTED', 'TRANSMIT', @Notes, @User, GETDATE())",
                new { Car = car, Notes = $"Kirim ke CEISA 4.0. Respon NPE: {npe}", User = username });

            await _audit.LogAsync(username, "TRANSMIT_CEISA_PEB", "CEISA", car, $"Dokumen PEB berhasil dikirim ke CEISA 4.0. Nopen: {nopen}, NPE: {npe}");
            await _notification.NotifyDocumentStatusChangeAsync("PEB", car, $"NPE TERBIT ({npe})", $"Dokumen PEB disetujui Bea Cukai dengan nomor NPE: {npe}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transmitting PEB {Car} to CEISA", car);
            result.Success = false;
            result.Message = $"Gagal transmisi: {ex.Message}";
            return result;
        }
    }

    public async Task<CeisaBc11ManifestData> PullBc11ManifestAsync(CeisaBc11PullRequest req, string username)
    {
        // Realistic simulated BC 1.1 pull
        var data = new CeisaBc11ManifestData
        {
            NoBc11 = req.NoBc11,
            TglBc11 = req.TglBc11?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
            PosNo = string.IsNullOrWhiteSpace(req.PosNo) ? "0001" : req.PosNo,
            SubPosNo = string.IsNullOrWhiteSpace(req.SubPosNo) ? "0000" : req.SubPosNo,
            NamaPengangkut = "WAN HAI 315 VOY. 042S",
            NoVoyage = "WH315-042S",
            PelabuhanMuat = string.IsNullOrWhiteSpace(req.PelMuat) ? "JPTYO" : req.PelMuat,
            PelabuhanBongkar = string.IsNullOrWhiteSpace(req.PelBongkar) ? "IDTPP" : req.PelBongkar,
            PelabuhanTransit = "SGSIN",
            NamaPemasok = "SUZUKI MOTOR CORPORATION JAPAN",
            NamaImportir = "PT. SUZUKI INDOMOBIL MOTOR",
            Bruto = "18450.00",
            JumlahKemasan = "24",
            JenisKemasan = "PK",
            NoContainer = "WHLU9283711",
            UkuranContainer = "40"
        };

        if (!string.IsNullOrWhiteSpace(req.Car))
        {
            await _db.ExecuteAsync(
                @"UPDATE PIB_DOIT_FINAL_HEADER 
                  SET NO_BC11 = @NoBc11, TGL_BC11 = @TglBc11, NO_POS_BC11 = @PosNo, NO_SUB_POS = @SubPosNo,
                      NM_ANGKUT = @NamaPengangkut, NO_VOY_FLIGHT = @NoVoyage, PEL_MUAT = @PelMuat, PEL_BONGKAR = @PelBongkar,
                      BRUTO = @Bruto
                  WHERE CAR = @Car",
                new
                {
                    NoBc11 = data.NoBc11,
                    TglBc11 = data.TglBc11,
                    PosNo = data.PosNo,
                    SubPosNo = data.SubPosNo,
                    NamaPengangkut = data.NamaPengangkut,
                    NoVoyage = data.NoVoyage,
                    PelMuat = data.PelabuhanMuat,
                    PelBongkar = data.PelabuhanBongkar,
                    Bruto = data.Bruto,
                    Car = req.Car
                });

            await _audit.LogAsync(username, "PULL_BC11", "CEISA", req.Car, $"Tarik data manifes BC 1.1 No. {req.NoBc11} Pos {data.PosNo} berhasil.");
        }

        return data;
    }

    public async Task<CeisaStatusTrackingResponse> GetTrackingTimelineAsync(string car, string docType)
    {
        var isPib = docType.Equals("PIB", StringComparison.OrdinalIgnoreCase);
        var tracking = new CeisaStatusTrackingResponse
        {
            Car = car,
            DocumentType = docType.ToUpper(),
            CustomsOffice = "KPU Bea dan Cukai Tipe A Tanjung Priok (010100)",
            LastUpdated = DateTime.Now
        };

        var history = await _db.QueryAsync<ApprovalLogModel>(
            "SELECT * FROM DOIT_APPROVAL_LOG WHERE CAR = @Car ORDER BY ACTION_DATE ASC",
            new { Car = car });

        var logs = history.ToList();

        // 1. Perekaman Dokumen
        tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
        {
            StepName = "Perekaman Dokumen (Draft)",
            Status = "COMPLETED",
            Timestamp = logs.FirstOrDefault()?.ActionDate ?? DateTime.Now.AddHours(-2),
            Description = "Dokumen dibuat dan disimpan di sistem internal Do-IT G2."
        });

        // 2. Review Internal
        var submitLog = logs.FirstOrDefault(l => l.Action == "SUBMIT");
        tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
        {
            StepName = "Pemeriksaan Dokumen Internal",
            Status = submitLog != null ? "COMPLETED" : "PENDING",
            Timestamp = submitLog?.ActionDate,
            Officer = submitLog?.ActionBy ?? "-",
            Description = submitLog != null ? $"Diajukan oleh {submitLog.ActionBy} untuk review kepatuhan pabean." : "Menunggu pengajuan review dari staf."
        });

        // 3. Approval Supervisor
        var approveLog = logs.FirstOrDefault(l => l.Action == "APPROVE");
        tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
        {
            StepName = "Persetujuan Supervisor / Manager",
            Status = approveLog != null ? "COMPLETED" : (submitLog != null ? "ACTIVE" : "PENDING"),
            Timestamp = approveLog?.ActionDate,
            Officer = approveLog?.ActionBy ?? "-",
            Description = approveLog != null ? $"Disetujui oleh Supervisor {approveLog.ActionBy}. Siap kirim ke Bea Cukai." : "Menunggu persetujuan supervisor."
        });

        // 4. Transmisi ke Portal CEISA 4.0
        var transmitLog = logs.FirstOrDefault(l => l.Action == "TRANSMIT");
        tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
        {
            StepName = "Transmisi Dokumen ke CEISA 4.0 Bea Cukai",
            Status = transmitLog != null ? "COMPLETED" : (approveLog != null ? "ACTIVE" : "PENDING"),
            Timestamp = transmitLog?.ActionDate,
            Officer = "CEISA Gateway API",
            Description = transmitLog != null ? "Payload dokumen berhasil dikirim dan diotentikasi oleh gateway CEISA 4.0." : "Menunggu pengiriman ke CEISA."
        });

        // 5. Penetapan Nomor & Respons Resmi (SPPB / NPE)
        if (isPib)
        {
            var pibRes = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT NO_PEN_PIB, NO_SPPB, TGL_SPPB FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
            var hasSppb = !string.IsNullOrWhiteSpace((string?)pibRes?.NO_SPPB);

            tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
            {
                StepName = "Penerbitan SPPB / Penetapan Jalur Pengeluaran",
                Status = hasSppb ? "COMPLETED" : "PENDING",
                Timestamp = pibRes?.TGL_SPPB,
                DocumentRef = (string?)pibRes?.NO_SPPB,
                Description = hasSppb ? $"Surat Persetujuan Pengeluaran Barang (SPPB) Terbit No: {pibRes?.NO_SPPB} (Jalur Hijau)" : "Menunggu verifikasi sistemik dan clearance Bea Cukai."
            });
            tracking.CurrentStatus = hasSppb ? "SPPB TERBIT (SELESAI)" : (transmitLog != null ? "PROSES CEISA" : (approveLog != null ? "APPROVED" : "DRAFT"));
        }
        else
        {
            var pebRes = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT NOPEN, STATUS FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
            var hasNpe = (int?)(pebRes?.STATUS ?? 0) >= 3;

            tracking.TrackingTimeline.Add(new CeisaStatusTrackingItem
            {
                StepName = "Penerbitan NPE (Nota Pelayanan Ekspor)",
                Status = hasNpe ? "COMPLETED" : "PENDING",
                Timestamp = DateTime.Now,
                DocumentRef = (string?)pebRes?.NOPEN,
                Description = hasNpe ? $"Nota Pelayanan Ekspor (NPE) Terbit No: {pebRes?.NOPEN}. Barang siap dimuat." : "Menunggu penetapan NPE."
            });
            tracking.CurrentStatus = hasNpe ? "NPE TERBIT (SELESAI)" : (transmitLog != null ? "PROSES CEISA" : (approveLog != null ? "APPROVED" : "DRAFT"));
        }

        return tracking;
    }
}
