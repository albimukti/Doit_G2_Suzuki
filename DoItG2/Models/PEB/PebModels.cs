using System;
using System.Collections.Generic;
using DoItG2.Models.Common;

namespace DoItG2.Models.PEB;

public class PebHeaderModel
{
    public string Car { get; set; } = string.Empty;              // Nomor AJU (primary key)
    public string Entity { get; set; } = "SIM";                  // SIM / SIS
    public string NamaBeli { get; set; } = string.Empty;         // Buyer name
    public string AlmtBeli { get; set; } = string.Empty;         // Buyer address
    public string NegBeli { get; set; } = string.Empty;           // Buyer country code
    public DateTime? TgEks { get; set; }                         // Export date
    public decimal Netto { get; set; }
    public decimal Bruto { get; set; }
    public decimal Fob { get; set; }
    public string Nopen { get; set; } = string.Empty;            // Reg number
    public DateTime? TglNopen { get; set; }
    public string KdKtr { get; set; } = string.Empty;            // Customs office
    public string Snrf { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT";                // DRAFT, SENT, APPROVED, REJECTED
    
    // Identitas Eksportir
    public string NamaEks { get; set; } = "PT. SUZUKI INDOMOBIL MOTOR";
    public string AlmtEks { get; set; } = "JL. RAYA PENGGILINGAN KM. 19";
    public string NpwpEks { get; set; } = "011297371411000";
    
    // Other fields mapping to the database
    public string Carrier { get; set; } = string.Empty;
    public string Voy { get; set; } = string.Empty;
    public string PelMuat { get; set; } = string.Empty;
    public string PelBongkar { get; set; } = string.Empty;
    public string PelTransit { get; set; } = string.Empty;
    public string NoInv { get; set; } = string.Empty;
    public DateTime? TgInv { get; set; }
    public string KdVal { get; set; } = "USD";
    public int JmCont { get; set; }
    public int JmBrg { get; set; }
    public string TtdPeb { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = string.Empty;

    // Workflow & Approval
    public string ApprovalStatus { get; set; } = "DRAFT"; // DRAFT, PENDING, APPROVED, REJECTED, TRANSMITTED, COMPLETED
    public string? ReviewNotes { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }

    // Collections
    public List<PebDetailModel> Details { get; set; } = [];
    public List<PebDocumentModel> Documents { get; set; } = [];
    public List<PebContainerModel> Containers { get; set; } = [];
    public List<PebResponModel> Responses { get; set; } = [];
    public List<ApprovalLogModel> ApprovalLogs { get; set; } = [];
}

public class PebDetailModel
{
    public int Id { get; set; }
    public string Car { get; set; } = string.Empty;
    public int Seri { get; set; }
    public string KdBrg { get; set; } = string.Empty;
    public string UrBrg { get; set; } = string.Empty;
    public string HsNo { get; set; } = string.Empty;
    public decimal JmlSat { get; set; }
    public string KdSat { get; set; } = string.Empty;
    public decimal NettoDet { get; set; }
    public decimal FobDet { get; set; }
}

public class PebDocumentModel
{
    public int Id { get; set; }
    public string Car { get; set; } = string.Empty;
    public int Seri { get; set; }
    public string KdDok { get; set; } = string.Empty;
    public string NoDok { get; set; } = string.Empty;
    public DateTime? TgDok { get; set; }
}

public class PebContainerModel
{
    public string Car { get; set; } = string.Empty;
    public string NoCont { get; set; } = string.Empty;
    public string UkurCont { get; set; } = string.Empty;  // 20, 40, etc.
    public string TipeCont { get; set; } = string.Empty;  // FCL, LCL
}

public class PebResponModel
{
    public string Car { get; set; } = string.Empty;
    public string ResKd { get; set; } = string.Empty;
    public DateTime? ResTg { get; set; }
    public string NoPen { get; set; } = string.Empty;
    public DateTime? TgPen { get; set; }
    public string Deskripsi { get; set; } = string.Empty;
}

public class PebListViewModel
{
    public List<PebHeaderModel> Items { get; set; } = [];
    public PaginationModel Pagination { get; set; } = new();
}
