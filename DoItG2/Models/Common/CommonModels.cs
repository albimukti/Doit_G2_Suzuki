namespace DoItG2.Models.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public int StatusCode { get; set; } = 200;

    public static ApiResponse<T> Ok(T data, string message = "Berhasil") => new()
    {
        Success = true, Message = message, Data = data
    };
    public static ApiResponse<T> Fail(string message, int code = 400) => new()
    {
        Success = false, Message = message, StatusCode = code
    };
}

public class PaginationModel
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRecords { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalRecords / PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
    public string? Search { get; set; }
    public string? FilterStatus { get; set; }
    public string? FilterType { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? SortBy { get; set; }
    public string SortDir { get; set; } = "DESC";
}

public class AuditLogModel
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsError { get; set; }
}

public class MasterPartModel
{
    public int Id { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string? HsCode { get; set; }
    public string? Satuan { get; set; }
    public string? Subinventory { get; set; }
    public string? Plant { get; set; }
    public string? NegAsal { get; set; }
    public bool IsActive { get; set; }
}

public class MasterDokumenModel
{
    public int Id { get; set; }
    public string KdDok { get; set; } = string.Empty;
    public string NmDok { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class MasterSupplierModel
{
    public int Id { get; set; }
    public string KdPemasok { get; set; } = string.Empty;
    public string NmPemasok { get; set; } = string.Empty;
    public string? AlmPemasok { get; set; }
    public string? NegPemasok { get; set; } = "JP";
    public bool IsActive { get; set; } = true;
}

public class MasterBuyerModel
{
    public int Id { get; set; }
    public string KdPembeli { get; set; } = string.Empty;
    public string NmPembeli { get; set; } = string.Empty;
    public string? AlmPembeli { get; set; }
    public string? NegPembeli { get; set; } = "JP";
    public bool IsActive { get; set; } = true;
}

public class MasterFasilitasModel
{
    public int Id { get; set; }
    public string NoSkep { get; set; } = string.Empty;
    public DateTime? TglSkep { get; set; }
    public string JenisFasilitas { get; set; } = "KITE";
    public string? Deskripsi { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MasterLartasModel
{
    public int Id { get; set; }
    public string NoPi { get; set; } = string.Empty;
    public string Komoditas { get; set; } = string.Empty;
    public decimal KuotaAwal { get; set; }
    public decimal KuotaTerpakai { get; set; }
    public decimal SisaKuota => KuotaAwal - KuotaTerpakai;
    public string Satuan { get; set; } = "KG";
    public DateTime? TglBerlaku { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MasterPkbModel
{
    public int Id { get; set; }
    public string PibType { get; set; } = "81";
    public string Car { get; set; } = string.Empty;
    public string Fasilitas { get; set; } = string.Empty;
    public string Gudang { get; set; } = string.Empty;
    public string? Petugas { get; set; }
    public string? NoPhone { get; set; }
    public string? AlmtSiap { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DashboardStats
{
    public int TotalPibToday { get; set; }
    public int TotalPebToday { get; set; }
    public int PibPending { get; set; }
    public int PebPending { get; set; }
    public int PibApproved { get; set; }
    public int PebApproved { get; set; }
    public int PibRejected { get; set; }
    public int PebRejected { get; set; }
    public int TotalPibMonth { get; set; }
    public int TotalPebMonth { get; set; }

    // Customs clearance channels & facilities
    public int JalurHijauCount { get; set; }
    public int JalurKuningCount { get; set; }
    public int JalurMerahCount { get; set; }
    public int MitaCount { get; set; }
    public decimal PibDutySaved { get; set; }
    public decimal PebExportValue { get; set; }

    // System performance & integration
    public double SyncSuccessRate { get; set; } = 99.8;
    public int AvgProcessingTimeMs { get; set; } = 142;

    public List<ChartDataPoint> PibMonthlyChart { get; set; } = [];
    public List<ChartDataPoint> PebMonthlyChart { get; set; } = [];
    public List<RecentActivity> RecentActivities { get; set; } = [];
}

public class ChartDataPoint
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class RecentActivity
{
    public string DocumentId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // PIB / PEB
    public string Status { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
}
