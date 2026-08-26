using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.PIB;
using DoItG2.Models.PEB;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DoItG2.Services;

public interface IPdfReportService
{
    Task<byte[]> GeneratePibPdfAsync(string car);
    Task<byte[]> GeneratePebPdfAsync(string car);
}

public class PdfReportService : IPdfReportService
{
    private readonly DatabaseContext _db;

    public PdfReportService(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<byte[]> GeneratePibPdfAsync(string car)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
        var details = (await _db.QueryAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_DETAIL WHERE CAR = @Car ORDER BY SERIAL ASC", new { Car = car })).ToList();
        var documents = (await _db.QueryAsync<dynamic>(
            "SELECT * FROM PIB_DOIT_FINAL_DOCUMENT WHERE CAR = @Car", new { Car = car })).ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("REPUBLIK INDONESIA - DIREKTORAT JENDERAL BEA DAN CUKAI").Bold().FontSize(10);
                            c.Item().Text("PEMBERITAHUAN IMPOR BARANG (BC 2.0)").Bold().FontSize(13).FontColor("#1e3a8a");
                            c.Item().Text($"Nomor Pengajuan (CAR): {car}").Bold().FontSize(9);
                        });
                        row.ConstantItem(150).Column(c =>
                        {
                            c.Item().Border(1).BorderColor("#94a3b8").Padding(4).Column(box =>
                            {
                                box.Item().Text($"No. Pendaftaran: {header?.NO_PEN_PIB ?? "-"}").Bold();
                                box.Item().Text($"Tgl. Daftar: {header?.TGL_PEND_PIB ?? "-"}");
                                box.Item().Text($"Kantor Pabean: {header?.KD_KANTOR ?? "010100"}");
                            });
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#1e3a8a");
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    // Section 1: Entitas
                    col.Item().Text("A. IDENTITAS PEMBERITAHUAN").Bold().FontColor("#1e3a8a");
                    col.Item().Border(1).BorderColor("#cbd5e1").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Importir: {header?.NM_IMO ?? "PT. SUZUKI INDOMOBIL MOTOR"}").Bold();
                            c.Item().Text($"NPWP: {header?.ID_IMP ?? "011297371411000"}");
                            c.Item().Text($"Alamat: {header?.AL_IMP ?? "JL. RAYA PENGGILINGAN KM 19"}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Pemasok: {header?.NM_PEMASOK ?? "-"}").Bold();
                            c.Item().Text($"Negara Asal: {header?.NEG_PEMASOK ?? "JP"}");
                            c.Item().Text($"Alamat Pemasok: {header?.AL_PEMASOK ?? "-"}");
                        });
                    });

                    col.Item().PaddingTop(8);

                    // Section 2: Pengangkutan & Dokumen
                    col.Item().Text("B. PENGANGKUTAN & NILAI TRANSAKSI").Bold().FontColor("#1e3a8a");
                    col.Item().Border(1).BorderColor("#cbd5e1").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Sarana Pengangkut: {header?.NM_ANGKUT ?? "-"}");
                            c.Item().Text($"No. Voy/Flight: {header?.NO_VOY_FLIGHT ?? "-"}");
                            c.Item().Text($"Pel. Bongkar: {header?.PEL_BONGKAR ?? "IDTPP"}");
                            c.Item().Text($"No. BC 1.1: {header?.NO_BC11 ?? "-"} Pos: {header?.NO_POS_BC11 ?? "-"}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Valuta: {header?.KD_VAL ?? "USD"} | NDPBM: {header?.NDPBM ?? "16,250.00"}");
                            c.Item().Text($"FOB: {header?.FOB ?? "0.00"}");
                            c.Item().Text($"Asuransi: {header?.ASURANSI ?? "0.00"} | Freight: {header?.FREIGHT ?? "0.00"}");
                            c.Item().Text($"CIF: {header?.CIF ?? "0.00"}").Bold();
                        });
                    });

                    col.Item().PaddingTop(8);

                    // Section 3: Data Barang
                    col.Item().Text("C. RINCIAN BARANG IMPOR").Bold().FontColor("#1e3a8a");
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(25);
                            columns.ConstantColumn(75);
                            columns.RelativeColumn();
                            columns.ConstantColumn(45);
                            columns.ConstantColumn(65);
                            columns.ConstantColumn(70);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("No").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("HS Code").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Uraian Barang").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Jumlah").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Harga Satuan").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Total CIF").Bold();
                        });

                        int idx = 1;
                        foreach (var itm in details)
                        {
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text(idx++.ToString());
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text((string)(itm.HS_NO ?? ""));
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text((string)(itm.GOOD_DESC1 ?? ""));
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.QUANTITY} {itm.UNIT_TYPE}");
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.UNIT_VAL:N2}");
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.CIF_PER_UNIT:N2}");
                        }
                    });

                    col.Item().PaddingTop(8);

                    // Section 4: Ringkasan Pungutan
                    col.Item().Text("D. PERHITUNGAN PUNGUTAN PAJAK & BEA").Bold().FontColor("#1e3a8a");
                    col.Item().Border(1).BorderColor("#cbd5e1").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Nilai Pabean (IDR): Rp {Convert.ToDecimal(header?.NILAI_PABEAN ?? 0):N0}").Bold();
                            c.Item().Text($"Bea Masuk (BM): Rp {Convert.ToDecimal(header?.TOTAL_BM ?? 0):N0}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"PPN (11%): Rp {Convert.ToDecimal(header?.TOTAL_PPN ?? 0):N0}");
                            c.Item().Text($"PPh Pasal 22: Rp {Convert.ToDecimal(header?.TOTAL_PPH ?? 0):N0}");
                            c.Item().Text($"Total Pungutan: Rp {Convert.ToDecimal(header?.TOTAL_PUNGUTAN ?? 0):N0}").Bold().FontColor("#16a34a");
                        });
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Dicetak otomatis oleh Do-IT G2 Suzuki Customs Platform | {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#64748b");
                    row.ConstantItem(80).AlignRight().Text(x =>
                    {
                        x.Span("Halaman ");
                        x.CurrentPageNumber();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    public async Task<byte[]> GeneratePebPdfAsync(string car)
    {
        var header = await _db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_HEADER WHERE CAR = @Car", new { Car = car });
        var details = (await _db.QueryAsync<dynamic>(
            "SELECT * FROM PEB_DOIT_FINAL_DETAIL WHERE CAR = @Car ORDER BY SERIBRG ASC", new { Car = car })).ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("REPUBLIK INDONESIA - DIREKTORAT JENDERAL BEA DAN CUKAI").Bold().FontSize(10);
                            c.Item().Text("PEMBERITAHUAN EKSPOR BARANG (BC 3.0)").Bold().FontSize(13).FontColor("#1e3a8a");
                            c.Item().Text($"Nomor Pengajuan (CAR): {car}").Bold().FontSize(9);
                        });
                        row.ConstantItem(150).Column(c =>
                        {
                            c.Item().Border(1).BorderColor("#94a3b8").Padding(4).Column(box =>
                            {
                                box.Item().Text($"No. NPE: {header?.NOPEN ?? "-"}").Bold();
                                box.Item().Text($"Tgl. Ekspor: {header?.TGEKS ?? "-"}");
                                box.Item().Text($"Kantor Pabean: {header?.KDKTR ?? "010100"}");
                            });
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#1e3a8a");
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Text("A. IDENTITAS EKSPORTIR & PEMBELI").Bold().FontColor("#1e3a8a");
                    col.Item().Border(1).BorderColor("#cbd5e1").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Eksportir: {header?.NAMAEKS ?? "PT. SUZUKI INDOMOBIL MOTOR"}").Bold();
                            c.Item().Text($"NPWP: {header?.NPWPEKS ?? "011297371411000"}");
                            c.Item().Text($"Alamat: {header?.ALMTEKS ?? "JL. RAYA PENGGILINGAN KM 19"}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Pembeli / Buyer: {header?.NAMABELI ?? "-"}").Bold();
                            c.Item().Text($"Negara Tujuan: {header?.NEGBELI ?? "JP"}");
                            c.Item().Text($"Alamat: {header?.ALMTBELI ?? "-"}");
                        });
                    });

                    col.Item().PaddingTop(8);

                    col.Item().Text("B. PENGANGKUTAN & NILAI EKSPOR").Bold().FontColor("#1e3a8a");
                    col.Item().Border(1).BorderColor("#cbd5e1").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Sarana Pengangkut: {header?.CARRIER ?? "-"}");
                            c.Item().Text($"Pel. Muat: {header?.PELMUAT ?? "-"}");
                            c.Item().Text($"Pel. Bongkar: {header?.PELBONGKAR ?? "-"}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Valuta: {header?.KDVAL ?? "USD"}");
                            c.Item().Text($"FOB Ekspor: {header?.FOB ?? "0.00"}").Bold();
                            c.Item().Text($"Netto: {header?.NETTO ?? "0.00"} Kg | Bruto: {header?.BRUTO ?? "0.00"} Kg");
                        });
                    });

                    col.Item().PaddingTop(8);

                    col.Item().Text("C. RINCIAN BARANG EKSPOR").Bold().FontColor("#1e3a8a");
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(25);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(75);
                            columns.ConstantColumn(80);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("No").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("HS Code").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Uraian Barang").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Jumlah").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("Netto (Kg)").Bold();
                            h.Cell().Background("#f1f5f9").Border(1).BorderColor("#cbd5e1").Padding(4).Text("FOB (USD)").Bold();
                        });

                        int idx = 1;
                        foreach (var itm in details)
                        {
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text(idx++.ToString());
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text((string)(itm.HS ?? ""));
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text((string)(itm.URBRG ?? ""));
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.JMLSAT} {itm.KDSAT}");
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.NETTODET:N2}");
                            table.Cell().Border(1).BorderColor("#e2e8f0").Padding(3).Text($"{itm.FOBDET:N2}");
                        }
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Dicetak otomatis oleh Do-IT G2 Suzuki Customs Platform | {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#64748b");
                    row.ConstantItem(80).AlignRight().Text(x =>
                    {
                        x.Span("Halaman ");
                        x.CurrentPageNumber();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
