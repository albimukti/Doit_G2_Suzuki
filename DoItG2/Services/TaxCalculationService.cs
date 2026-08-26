using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.Common;
using DoItG2.Models.PIB;
using Microsoft.Extensions.Logging;

namespace DoItG2.Services;

public interface ITaxCalculationService
{
    Task<decimal> GetCurrentNdpbmAsync(string kdVal);
    Task<IEnumerable<MasterKursModel>> GetAllActiveRatesAsync();
    Task<TaxCalculationResult> CalculatePibTaxAsync(string car);
    Task<TaxCalculationResult> CalculatePibTaxPreviewAsync(string kdVal, decimal fob, decimal asuransi, decimal freight, decimal bmTarifPct, decimal ppnTarifPct = 11.0m, decimal pphTarifPct = 2.5m);
    Task<bool> SaveCalculatedTaxToPibHeaderAsync(string car, TaxCalculationResult result);
    Task<bool> UpdateRateAsync(string kdVal, decimal nilaiNdpbm, DateTime tglAwal, DateTime tglAkhir, string noKmk);
}

public class TaxCalculationService : ITaxCalculationService
{
    private readonly DatabaseContext _db;
    private readonly ILogger<TaxCalculationService> _logger;

    public TaxCalculationService(DatabaseContext db, ILogger<TaxCalculationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<decimal> GetCurrentNdpbmAsync(string kdVal)
    {
        if (string.IsNullOrWhiteSpace(kdVal) || kdVal.Equals("IDR", StringComparison.OrdinalIgnoreCase))
            return 1.0m;

        var sql = @"SELECT TOP 1 NILAI_NDPBM 
                    FROM DOIT_KURS_PAJAK 
                    WHERE KD_VAL = @KdVal AND IS_ACTIVE = 1 
                    ORDER BY TGL_AWAL DESC";
        
        var rate = await _db.ExecuteScalarAsync<decimal?>(sql, new { KdVal = kdVal.Trim().ToUpper() });
        if (rate.HasValue && rate.Value > 0)
            return rate.Value;

        // Sensible fallbacks if not found in database
        return kdVal.Trim().ToUpper() switch
        {
            "USD" => 16250.0m,
            "JPY" => 10650.0m, // per 100 JPY
            "EUR" => 17480.0m,
            "SGD" => 12150.0m,
            "CNY" => 2240.0m,
            "AUD" => 10500.0m,
            "GBP" => 20500.0m,
            "THB" => 450.0m,
            _ => 1.0m
        };
    }

    public async Task<IEnumerable<MasterKursModel>> GetAllActiveRatesAsync()
    {
        var sql = @"SELECT ID, KD_VAL AS KdVal, NM_VAL AS NmVal, NILAI_NDPBM AS NilaiNdpbm, 
                           TGL_AWAL AS TglAwal, TGL_AKHIR AS TglAkhir, NO_KMK AS NoKmk, 
                           IS_ACTIVE AS IsActive, CREATED_AT AS CreatedAt, UPDATED_AT AS UpdatedAt
                    FROM DOIT_KURS_PAJAK 
                    ORDER BY KD_VAL ASC";
        return await _db.QueryAsync<MasterKursModel>(sql);
    }

    public async Task<TaxCalculationResult> CalculatePibTaxPreviewAsync(string kdVal, decimal fob, decimal asuransi, decimal freight, decimal bmTarifPct, decimal ppnTarifPct = 11.0m, decimal pphTarifPct = 2.5m)
    {
        var ndpbm = await GetCurrentNdpbmAsync(kdVal);
        var cifValas = fob + asuransi + freight;
        
        // If JPY, NDPBM is per 100 JPY in Indonesia customs standard
        var effectiveNdpbm = kdVal.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? (ndpbm / 100.0m) : ndpbm;
        var cifIdr = Math.Round(cifValas * effectiveNdpbm, MidpointRounding.AwayFromZero);
        
        var bmIdr = Math.Round(cifIdr * (bmTarifPct / 100.0m), MidpointRounding.AwayFromZero);
        var nilaiImpor = cifIdr + bmIdr;
        var ppnIdr = Math.Round(nilaiImpor * (ppnTarifPct / 100.0m), MidpointRounding.AwayFromZero);
        var pphIdr = Math.Round(nilaiImpor * (pphTarifPct / 100.0m), MidpointRounding.AwayFromZero);

        return new TaxCalculationResult
        {
            Valuta = kdVal,
            Ndpbm = ndpbm,
            FobValas = fob,
            AsuransiValas = asuransi,
            FreightValas = freight,
            CifValas = cifValas,
            CifIdr = cifIdr,
            BeaMasukTarif = bmTarifPct,
            BeaMasukIdr = bmIdr,
            PpnTarif = ppnTarifPct,
            PpnIdr = ppnIdr,
            PphTarif = pphTarifPct,
            PphIdr = pphIdr,
            TotalDibebaskan = 0,
            TotalDitanggung = 0
        };
    }

    public async Task<TaxCalculationResult> CalculatePibTaxAsync(string car)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT CAR, KD_VAL, FOB, ASURANSI, FREIGHT, CIF, NDPBM FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car",
            new { Car = car });

        if (header == null)
            return new TaxCalculationResult { Car = car };

        string kdVal = header.KD_VAL?.ToString() ?? "USD";
        decimal.TryParse(header.FOB?.ToString(), out decimal fob);
        decimal.TryParse(header.ASURANSI?.ToString(), out decimal asuransi);
        decimal.TryParse(header.FREIGHT?.ToString(), out decimal freight);
        decimal.TryParse(header.CIF?.ToString(), out decimal cif);

        if (cif <= 0 && fob > 0)
            cif = fob + asuransi + freight;

        // Check if items have specific tariff rates or facility exemptions (e.g. KITE)
        var items = (await _db.QueryAsync<dynamic>(
            "SELECT SERIAL, CIF_PER_UNIT, QUANTITY, UNIT_VAL, KD_FAS FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car",
            new { Car = car })).ToList();

        decimal avgBmTarif = 5.0m;
        bool isKiteExempt = items.Any(i => string.Equals(i.KD_FAS?.ToString(), "KITE", StringComparison.OrdinalIgnoreCase));

        var result = await CalculatePibTaxPreviewAsync(kdVal, fob, asuransi, freight, avgBmTarif, 11.0m, 2.5m);
        result.Car = car;

        if (isKiteExempt)
        {
            result.TotalDibebaskan = result.BeaMasukIdr + result.PpnIdr;
        }

        return result;
    }

    public async Task<bool> SaveCalculatedTaxToPibHeaderAsync(string car, TaxCalculationResult result)
    {
        try
        {
            var sql = @"UPDATE PIB_DOIT_FINAL_HEADER 
                        SET FOB = @Fob, ASURANSI = @Asuransi, FREIGHT = @Freight, CIF = @Cif, 
                            NDPBM = @Ndpbm, KD_VAL = @KdVal,
                            TOTAL_BM = @TotalBm, TOTAL_PPN = @TotalPpn, TOTAL_PPH = @TotalPph, 
                            TOTAL_PUNGUTAN = @TotalPungutan, NILAI_PABEAN = @NilaiPabean
                        WHERE CAR = @Car";

            var rows = await _db.ExecuteAsync(sql, new
            {
                Fob = result.FobValas.ToString("F2"),
                Asuransi = result.AsuransiValas.ToString("F2"),
                Freight = result.FreightValas.ToString("F2"),
                Cif = result.CifValas.ToString("F2"),
                Ndpbm = result.Ndpbm.ToString("F2"),
                KdVal = result.Valuta,
                TotalBm = result.BeaMasukIdr,
                TotalPpn = result.PpnIdr,
                TotalPph = result.PphIdr,
                TotalPungutan = result.TotalBayar,
                NilaiPabean = result.NilaiPabean,
                Car = car
            });

            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving calculated tax to PIB header {Car}", car);
            return false;
        }
    }

    public async Task<bool> UpdateRateAsync(string kdVal, decimal nilaiNdpbm, DateTime tglAwal, DateTime tglAkhir, string noKmk)
    {
        try
        {
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM DOIT_KURS_PAJAK WHERE KD_VAL = @KdVal",
                new { KdVal = kdVal.Trim().ToUpper() });

            if (exists > 0)
            {
                var sql = @"UPDATE DOIT_KURS_PAJAK 
                            SET NILAI_NDPBM = @NilaiNdpbm, TGL_AWAL = @TglAwal, TGL_AKHIR = @TglAkhir, 
                                NO_KMK = @NoKmk, UPDATED_AT = GETDATE()
                            WHERE KD_VAL = @KdVal";
                await _db.ExecuteAsync(sql, new { KdVal = kdVal.Trim().ToUpper(), NilaiNdpbm = nilaiNdpbm, TglAwal = tglAwal, TglAkhir = tglAkhir, NoKmk = noKmk });
            }
            else
            {
                var sql = @"INSERT INTO DOIT_KURS_PAJAK (KD_VAL, NM_VAL, NILAI_NDPBM, TGL_AWAL, TGL_AKHIR, NO_KMK)
                            VALUES (@KdVal, @NmVal, @NilaiNdpbm, @TglAwal, @TglAkhir, @NoKmk)";
                await _db.ExecuteAsync(sql, new { KdVal = kdVal.Trim().ToUpper(), NmVal = kdVal.Trim().ToUpper(), NilaiNdpbm = nilaiNdpbm, TglAwal = tglAwal, TglAkhir = tglAkhir, NoKmk = noKmk });
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating exchange rate for {KdVal}", kdVal);
            return false;
        }
    }
}
