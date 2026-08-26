using DoItG2.Data;
using DoItG2.Models.PIB;

namespace DoItG2.Services;

public interface IValidationService
{
    Task<ValidationResult> ValidatePibAsync(string car);
    Task<ValidationResult> ValidatePebAsync(string car);
}

public class ValidationService : IValidationService
{
    private readonly DatabaseContext _db;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(DatabaseContext db, ILogger<ValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidatePibAsync(string car)
    {
        var result = new ValidationResult { DocumentId = car, DocumentType = "PIB" };

        try
        {
            // 1. Check header exists
            var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT CAR, KD_KANTOR, JNS_PIB, JNS_IMP, NM_PEMASOK, TGL_TIBA, JML_BRG FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = car });

            if (header == null)
            {
                result.Errors.Add(new ValidationError("HEADER", "Header dokumen PIB tidak ditemukan.", ValidationSeverity.Error));
                return result;
            }

            // 2. Check mandatory header fields
            if (string.IsNullOrWhiteSpace((string?)header.KD_KANTOR))
                result.Errors.Add(new ValidationError("HEADER", "Kode kantor pabean wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.NM_PEMASOK))
                result.Errors.Add(new ValidationError("ENTITAS", "Nama pemasok (supplier) wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.TGL_TIBA))
                result.Errors.Add(new ValidationError("PENGANGKUT", "Tanggal tiba (ETA) wajib diisi.", ValidationSeverity.Error));

            // 3. Check detail items exist
            var detailCount = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car", new { Car = car });

            if (detailCount == 0)
                result.Errors.Add(new ValidationError("BARANG", "Minimal 1 item barang harus diisi.", ValidationSeverity.Error));

            // 4. Validate HS codes format (must be 8-10 digits)
            var invalidHsCodes = await _db.QueryAsync<dynamic>(
                @"SELECT SERIAL, HS_NO FROM PIB_DOIT_FINAL_DETAIL 
                   WHERE CAR = @Car AND (LEN(HS_NO) < 8 OR HS_NO IS NULL OR HS_NO = '')",
                new { Car = car });

            foreach (var hs in invalidHsCodes)
            {
                result.Errors.Add(new ValidationError("BARANG",
                    $"Seri {hs.SERIAL}: HS Code '{hs.HS_NO}' tidak valid (minimal 8 digit).", ValidationSeverity.Error));
            }

            // 5. Check for documents
            var docCount = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PIB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car", new { Car = car });

            if (docCount == 0)
                result.Errors.Add(new ValidationError("DOKUMEN", "Dokumen pendukung (Invoice/BL) belum dilampirkan.", ValidationSeverity.Warning));

            // 6. Lartas warning — check if any HS codes match known lartas items
            var lartasItems = await _db.QueryAsync<dynamic>(
                @"SELECT d.SERIAL, d.HS_NO, d.GOOD_DESC1 
                   FROM PIB_DOIT_FINAL_DETAIL d 
                   WHERE d.CAR = @Car 
                   AND LEFT(d.HS_NO, 4) IN ('8703','8704','8711','2710','2711','3808','9013','8525')",
                new { Car = car });

            foreach (var item in lartasItems)
            {
                result.Errors.Add(new ValidationError("LARTAS",
                    $"Seri {item.SERIAL}: HS {item.HS_NO} termasuk barang Lartas — pastikan izin tersedia.",
                    ValidationSeverity.Warning));
            }

            // 7. Container check
            var containerCount = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PIB_DOIT_FINAL_CONTAINER WHERE CAR = @Car", new { Car = car });

            if (containerCount == 0)
                result.Errors.Add(new ValidationError("KEMASAN", "Data peti kemas (container) belum diisi.", ValidationSeverity.Info));

            // 8. Math Consistency & Weight Checks
            var values = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT FOB, ASURANSI, FREIGHT, CIF, NETTO, BRUTO FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = car });

            if (values != null)
            {
                string? fobStr = ((object?)values.FOB)?.ToString();
                string? asuransiStr = ((object?)values.ASURANSI)?.ToString();
                string? freightStr = ((object?)values.FREIGHT)?.ToString();
                string? cifStr = ((object?)values.CIF)?.ToString();
                string? nettoStr = ((object?)values.NETTO)?.ToString();
                string? brutoStr = ((object?)values.BRUTO)?.ToString();

                decimal.TryParse(fobStr, out decimal fob);
                decimal.TryParse(asuransiStr, out decimal asuransi);
                decimal.TryParse(freightStr, out decimal freight);
                decimal.TryParse(cifStr, out decimal cif);
                decimal.TryParse(nettoStr, out decimal netto);
                decimal.TryParse(brutoStr, out decimal bruto);

                if (fob > 0 && cif > 0 && Math.Abs((fob + asuransi + freight) - cif) > 1.0m)
                {
                    result.Errors.Add(new ValidationError("TRANSAKSI",
                        $"Inkonsistensi Nilai: CIF ({cif:N2}) tidak sama dengan FOB + Asuransi + Freight ({fob + asuransi + freight:N2}).",
                        ValidationSeverity.Warning));
                }

                if (netto > 0 && bruto > 0 && netto > bruto)
                {
                    result.Errors.Add(new ValidationError("KEMASAN",
                        $"Berat Netto ({netto:N2} Kg) tidak boleh melebihi Berat Bruto ({bruto:N2} Kg).",
                        ValidationSeverity.Error));
                }
            }

            result.IsValid = !result.Errors.Any(e => e.Severity == ValidationSeverity.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PIB {Car}", car);
            result.Errors.Add(new ValidationError("SYSTEM", $"Error validasi: {ex.Message}", ValidationSeverity.Error));
        }

        return result;
    }

    public async Task<ValidationResult> ValidatePebAsync(string car)
    {
        var result = new ValidationResult { DocumentId = car, DocumentType = "PEB" };

        try
        {
            // 1. Header check
            var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT CAR, NAMAEKS, ALMTEKS, NEGBELI, TGEKS, NETTO, BRUTO, FOB, KDKTR, PELMUAT, PELBONGKAR FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car",
                new { Car = car });

            if (header == null)
            {
                result.Errors.Add(new ValidationError("HEADER", "Header dokumen PEB tidak ditemukan.", ValidationSeverity.Error));
                return result;
            }

            // 2. Mandatory CEISA 4.0 Ekspor Header Fields
            if (string.IsNullOrWhiteSpace((string?)header.KDKTR))
                result.Errors.Add(new ValidationError("HEADER", "Kode kantor pabean ekspor wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.NAMAEKS))
                result.Errors.Add(new ValidationError("ENTITAS", "Nama eksportir wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.NEGBELI))
                result.Errors.Add(new ValidationError("ENTITAS", "Negara pembeli (buyer) wajib diisi (kode 2 huruf).", ValidationSeverity.Error));

            if (header.TGEKS == null)
                result.Errors.Add(new ValidationError("PENGANGKUT", "Tanggal perkiraan ekspor (ETD) wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.PELMUAT))
                result.Errors.Add(new ValidationError("PENGANGKUT", "Pelabuhan muat ekspor wajib diisi.", ValidationSeverity.Error));

            if (string.IsNullOrWhiteSpace((string?)header.PELBONGKAR))
                result.Errors.Add(new ValidationError("PENGANGKUT", "Pelabuhan tujuan/bongkar wajib diisi.", ValidationSeverity.Error));

            // 3. Weight & Values Check
            if (Convert.ToDecimal(header.FOB ?? 0) <= 0)
                result.Errors.Add(new ValidationError("TRANSAKSI", "Nilai FOB ekspor harus lebih dari 0.", ValidationSeverity.Error));

            if (Convert.ToDecimal(header.NETTO ?? 0) <= 0)
                result.Errors.Add(new ValidationError("TRANSAKSI", "Berat bersih (Netto) harus lebih dari 0 Kg.", ValidationSeverity.Error));

            // 4. Detail Items Check
            var detailCount = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PEB_DOIT_FINAL_DETAIL WHERE CAR = @Car", new { Car = car });

            if (detailCount == 0)
                result.Errors.Add(new ValidationError("BARANG", "Minimal 1 item barang ekspor harus diisi.", ValidationSeverity.Error));

            // 5. HS Code Validation for PEB (must be at least 8 digits)
            var invalidHsCodes = await _db.QueryAsync<dynamic>(
                @"SELECT SERIBRG, HS FROM PEB_DOIT_FINAL_DETAIL 
                   WHERE CAR = @Car AND (HS IS NULL OR LEN(CAST(HS AS VARCHAR)) < 8)",
                new { Car = car });

            foreach (var item in invalidHsCodes)
            {
                result.Errors.Add(new ValidationError("BARANG",
                    $"Seri {item.SERIBRG}: HS Code '{item.HS}' tidak valid untuk ekspor CEISA 4.0 (minimal 8 digit).",
                    ValidationSeverity.Error));
            }

            // 6. Documents Check
            var docCount = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PEB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car", new { Car = car });

            if (docCount == 0)
                result.Errors.Add(new ValidationError("DOKUMEN", "Dokumen pendukung ekspor (Invoice/Packing List) belum dilampirkan.", ValidationSeverity.Warning));

            result.IsValid = !result.Errors.Any(e => e.Severity == ValidationSeverity.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PEB {Car}", car);
            result.Errors.Add(new ValidationError("SYSTEM", $"Error validasi: {ex.Message}", ValidationSeverity.Error));
        }

        return result;
    }
}

public class ValidationResult
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = [];
    public int ErrorCount => Errors.Count(e => e.Severity == ValidationSeverity.Error);
    public int WarningCount => Errors.Count(e => e.Severity == ValidationSeverity.Warning);
    public int InfoCount => Errors.Count(e => e.Severity == ValidationSeverity.Info);
}

public class ValidationError
{
    public string Tab { get; set; }
    public string Message { get; set; }
    public ValidationSeverity Severity { get; set; }

    public ValidationError(string tab, string message, ValidationSeverity severity)
    {
        Tab = tab;
        Message = message;
        Severity = severity;
    }
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}
