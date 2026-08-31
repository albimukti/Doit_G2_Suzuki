using System;
using System.Collections.Generic;

namespace DoItG2.Models.CEISA;

public class CeisaTransmitRequest
{
    public string Car { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "PIB"; // PIB (BC 2.0) or PEB (BC 3.0)
    public bool SimulateSandbox { get; set; } = true;
    public string? Notes { get; set; }
}

public class CeisaTransmitResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Car { get; set; } = string.Empty;
    public string? Nopen { get; set; }
    public DateTime? TglNopen { get; set; }
    public string? NoSppb { get; set; }
    public DateTime? TglSppb { get; set; }
    public string? NoNpe { get; set; }
    public DateTime? TglNpe { get; set; }
    public string? BillingCode { get; set; }
    public decimal? BillingAmount { get; set; }
    public DateTime? BillingExpiry { get; set; }
    public string Channel { get; set; } = "HIJAU"; // HIJAU, KUNING, MERAH, MITA
    public string ResponseCode { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = string.Empty;
}

public class CeisaStatusTrackingItem
{
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // COMPLETED, ACTIVE, PENDING, ERROR
    public DateTime? Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Officer { get; set; }
    public string? DocumentRef { get; set; }
}

public class CeisaStatusTrackingResponse
{
    public string Car { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string CustomsOffice { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public List<CeisaStatusTrackingItem> TrackingTimeline { get; set; } = [];
}

public class CeisaBc11PullRequest
{
    public string NoBc11 { get; set; } = string.Empty;
    public DateTime? TglBc11 { get; set; }
    public string PosNo { get; set; } = "0001";
    public string SubPosNo { get; set; } = "0000";
    public string Car { get; set; } = string.Empty;
    public string? PelMuat { get; set; }
    public string? PelBongkar { get; set; }
}

public class CeisaBc11ManifestData
{
    public string NoBc11 { get; set; } = string.Empty;
    public string TglBc11 { get; set; } = string.Empty;
    public string PosNo { get; set; } = string.Empty;
    public string SubPosNo { get; set; } = string.Empty;
    public string NamaPengangkut { get; set; } = string.Empty;
    public string NoVoyage { get; set; } = string.Empty;
    public string PelabuhanMuat { get; set; } = string.Empty;
    public string PelabuhanBongkar { get; set; } = string.Empty;
    public string PelabuhanTransit { get; set; } = string.Empty;
    public string NamaPemasok { get; set; } = string.Empty;
    public string NamaImportir { get; set; } = string.Empty;
    public string Bruto { get; set; } = string.Empty;
    public string JumlahKemasan { get; set; } = string.Empty;
    public string JenisKemasan { get; set; } = string.Empty;
    public string NoContainer { get; set; } = string.Empty;
    public string UkuranContainer { get; set; } = string.Empty;
}

public class CeisaSendPibViewModel
{
    public List<DoItG2.Models.PIB.PibHeaderModel> Items { get; set; } = [];
    public string ActiveTab { get; set; } = "queue"; // queue, ready, failed, pending, history
    public int QueueCount { get; set; }
    public int ReadyCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public int HistoryCount { get; set; }
}

public class CeisaSendPebViewModel
{
    public List<DoItG2.Models.PEB.PebHeaderModel> Items { get; set; } = [];
    public string ActiveTab { get; set; } = "queue"; // queue, ready, failed, pending, history
    public int QueueCount { get; set; }
    public int ReadyCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public int HistoryCount { get; set; }
}

