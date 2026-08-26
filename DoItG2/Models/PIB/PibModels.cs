using DoItG2.Models.Common;

namespace DoItG2.Models.PIB;

public class PibHeaderModel
{
    public string Car { get; set; } = string.Empty;     // Nomor AJU (primary key)
    public string Status { get; set; } = string.Empty;  // Status dokumen
    public string PibNo { get; set; } = string.Empty;
    public string? PibTg { get; set; }
    public string? SppbNo { get; set; }
    public string? SppbTg { get; set; }
    
    // Identitas Importir
    public string KdIdImp { get; set; } = string.Empty;
    public string IdImp { get; set; } = string.Empty;
    public string NmImo { get; set; } = string.Empty;
    public string AlImp { get; set; } = string.Empty;
    public string StatusImp { get; set; } = string.Empty;
    public string KdApi { get; set; } = string.Empty;
    public string NoApi { get; set; } = string.Empty;
    
    // PPJK
    public string KdIdPpjk { get; set; } = string.Empty;
    public string IdPpjk { get; set; } = string.Empty;
    public string NmPpjk { get; set; } = string.Empty;
    public string AlPpjk { get; set; } = string.Empty;
    public string KdKtrPpjk { get; set; } = string.Empty;
    public string? TglSkepPpjk { get; set; }
    public string NoSkepPpjk { get; set; } = string.Empty;
    
    // Industri
    public string KdIdInd { get; set; } = string.Empty;
    public string IdInd { get; set; } = string.Empty;
    public string NmInd { get; set; } = string.Empty;
    public string AlInd { get; set; } = string.Empty;
    
    // Kepabeanan
    public string KdKantor { get; set; } = string.Empty;
    public string JnsPib { get; set; } = string.Empty;
    public string JnsImp { get; set; } = string.Empty;
    public string JnsBayar { get; set; } = string.Empty;
    public string KdSkepFas { get; set; } = string.Empty;
    
    // Pemasok
    public string NegPemasok { get; set; } = string.Empty;
    public string NmPemasok { get; set; } = string.Empty;
    public string AlPemasok { get; set; } = string.Empty;
    
    // Pengangkutan
    public string NmAngkut { get; set; } = string.Empty;
    public string CaraAngkut { get; set; } = string.Empty;
    public string PelMuat { get; set; } = string.Empty;
    public string PelBongkar { get; set; } = string.Empty;
    public string PelTransit { get; set; } = string.Empty;
    public string BenderaVoy { get; set; } = string.Empty;
    public string NoVoyFlight { get; set; } = string.Empty;
    public string? TglTiba { get; set; }
    public string Gudang { get; set; } = string.Empty;
    
    // BC11
    public string NoBc11 { get; set; } = string.Empty;
    public string NoPosBc11 { get; set; } = string.Empty;
    public string NoSubPos { get; set; } = string.Empty;
    public string? TglBc11 { get; set; }
    
    // Nilai
    public string KdVal { get; set; } = string.Empty;
    public string Ndpbm { get; set; } = string.Empty;
    public string Fob { get; set; } = string.Empty;
    public string Asuransi { get; set; } = string.Empty;
    public string Freight { get; set; } = string.Empty;
    public string Cif { get; set; } = string.Empty;
    public string Netto { get; set; } = string.Empty;
    public string Bruto { get; set; } = string.Empty;
    
    // Fasilitas
    public string JmlCont { get; set; } = string.Empty;
    public string LokBayar { get; set; } = string.Empty;
    public string JmlBrg { get; set; } = string.Empty;
    public string KdJaminan { get; set; } = string.Empty;
    
    // Meta
    public string TpName { get; set; } = string.Empty;
    public string EdiNumber { get; set; } = string.Empty;
    public string AsalData { get; set; } = string.Empty;
    public string? TglPendPib { get; set; }
    public string? TglSppb { get; set; }
    public DateTime? CreationDate { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? LastUpdateDate { get; set; }
    public int? LastUpdatedBy { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Rate { get; set; } = string.Empty;

    // Workflow & Approval
    public string ApprovalStatus { get; set; } = "DRAFT"; // DRAFT, PENDING, APPROVED, REJECTED, TRANSMITTED, COMPLETED
    public string? ReviewNotes { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }

    // Tax & Duty Totals
    public decimal TotalBm { get; set; }
    public decimal TotalPpn { get; set; }
    public decimal TotalPph { get; set; }
    public decimal TotalPungutan { get; set; }
    public decimal NilaiPabean { get; set; }

    // Collections
    public List<PibDetailModel> Details { get; set; } = [];
    public List<PibDocumentModel> Documents { get; set; } = [];
    public List<PibContainerModel> Containers { get; set; } = [];
    public List<PibPackageModel> Packages { get; set; } = [];
    public List<PibTaxModel> Taxes { get; set; } = [];
    public List<PibVehicleModel> Vehicles { get; set; } = [];
    public List<PibResponModel> Responses { get; set; } = [];
    public List<ApprovalLogModel> ApprovalLogs { get; set; } = [];
}

public class PibDetailModel
{
    public int Id { get; set; }
    public string Car { get; set; } = string.Empty;
    public int Serial { get; set; }
    public string HsNo { get; set; } = string.Empty;
    public string GoodDesc1 { get; set; } = string.Empty;
    public string GoodDesc2 { get; set; } = string.Empty;
    public string GoodDesc3 { get; set; } = string.Empty;
    public string OriginCountry { get; set; } = string.Empty;
    public decimal UnitVal { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal CifPerUnit { get; set; }
    public string KdFas { get; set; } = string.Empty;
    public decimal BmTarif { get; set; }
    public decimal BmNilai { get; set; }
    public decimal PpnTarif { get; set; }
    public decimal PpnNilai { get; set; }
    public decimal PphTarif { get; set; }
    public decimal PphNilai { get; set; }
}

public class PibDocumentModel
{
    public int Id { get; set; }
    public string Car { get; set; } = string.Empty;
    public int Serial { get; set; }
    public string DokKd { get; set; } = string.Empty;
    public string DokNm { get; set; } = string.Empty;
    public string DokNo { get; set; } = string.Empty;
    public string? DokTg { get; set; }
}

public class PibContainerModel
{
    public string Car { get; set; } = string.Empty;
    public string NoCont { get; set; } = string.Empty;
    public int UkrCont { get; set; }
    public string JnsMuat { get; set; } = string.Empty;
    public string JnsCont { get; set; } = string.Empty;
}

public class PibPackageModel
{
    public string Car { get; set; } = string.Empty;
    public int JmlKms { get; set; }
    public string MerkKms { get; set; } = string.Empty;
    public string JnsKms { get; set; } = string.Empty;
}

public class PibTaxModel
{
    public string Car { get; set; } = string.Empty;
    public string KdPungutan { get; set; } = string.Empty;
    public long Nilai { get; set; }
}

public class PibVehicleModel
{
    public string Car { get; set; } = string.Empty;
    public int Serial { get; set; }
    public string NoRangka { get; set; } = string.Empty;
    public string NoMesin { get; set; } = string.Empty;
    public double Silinder { get; set; }
    public string Tahun { get; set; } = string.Empty;
    public string FlagCbu { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
}

public class PibResponModel
{
    public string Car { get; set; } = string.Empty;
    public string ResKd { get; set; } = string.Empty;
    public string? ResTg { get; set; }
    public string DokResNo { get; set; } = string.Empty;
    public string? DokResTg { get; set; }
    public string Kpbc { get; set; } = string.Empty;
    public string PibNo { get; set; } = string.Empty;
    public string? PibTg { get; set; }
    public string Deskripsi { get; set; } = string.Empty;
    public bool Dibaca { get; set; }
    public string NamaImp { get; set; } = string.Empty;
    public string TotalBayar { get; set; } = string.Empty;
    public string Terbilang { get; set; } = string.Empty;
}

public class PibListViewModel
{
    public List<PibHeaderModel> Items { get; set; } = [];
    public PaginationModel Pagination { get; set; } = new();
}
