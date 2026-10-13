using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Saudization.Web.Models;

/// <summary>How the employee worksheets in an uploaded workbook are organized.</summary>
public enum ImportSheetRule
{
    PerFacility,      // one sheet per facility, named by the facility code (رقم المنشأة)
    PerRegistration   // one sheet per registration/company, named by its label (السجل التجاري)
}

/// <summary>Nitaqat colour zone (نطاقات). Ordered worst → best.</summary>
public enum NitaqatZone
{
    Unknown = 0,
    Red = 1,          // النطاق الأحمر
    LowGreen = 2,     // النطاق الأخضر المنخفض
    MediumGreen = 3,  // النطاق الأخضر المتوسط
    HighGreen = 4,    // النطاق الأخضر المرتفع
    Platinum = 5      // النطاق البلاتيني
}

/// <summary>Saudization category — فئة التوطين (from the "فئات التوطين" sheet).</summary>
public class SaudizationCategory
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string NameAr { get; set; } = "";      // الفئة

    [MaxLength(200)]
    public string? NameEn { get; set; }

    /// <summary>الحد الأدنى لحساب التوطين — headcount floor below which the category is exempt.</summary>
    public int MinForCalc { get; set; }

    /// <summary>النسبة الحالية — required Saudi ratio in force now (0..1).</summary>
    public decimal CurrentRequiredPct { get; set; }

    public string? CurrentDecisionDate { get; set; }   // تاريخ تطبيق القرار (Hijri text)

    /// <summary>نسبة التوطين المستقبلية — required Saudi ratio for the improvement plan (0..1).</summary>
    public decimal FutureRequiredPct { get; set; }

    public string? FutureDecisionDate { get; set; }     // تاريخ تطبيق القرار المستقبلي

    public decimal? MinWage { get; set; }               // الحد الأدنى للأجور

    public ICollection<Occupation> Occupations { get; set; } = new List<Occupation>();
}

/// <summary>Official occupation → category map — دليل المهن.</summary>
public class Occupation
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string? Code { get; set; }              // الرمز (SOC code)

    [Required, MaxLength(300)]
    public string NameAr { get; set; } = "";       // المهنة

    public int? CategoryId { get; set; }           // الفئة
    public SaudizationCategory? Category { get; set; }
}

/// <summary>Nationality lookup with the Saudi flag that drives every calculation.</summary>
public class Nationality
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string NameAr { get; set; } = "";       // e.g. سعودي / باكستاني

    [MaxLength(100)]
    public string? NameEn { get; set; }

    public bool IsSaudi { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>Record = commercial registration (السجل التجاري). Has ≥1 facility and its own Nitaqat thresholds.</summary>
public class Record
{
    public int Id { get; set; }

    /// <summary>السجل التجاري — short group label shown as a dashboard column (ATCL, Branch, MASCO Main…).</summary>
    [Required, MaxLength(100)]
    public string RegistrationLabel { get; set; } = "";

    [MaxLength(60)]
    public string? RegistrationNumber { get; set; }   // رقم السجل التجاري

    [MaxLength(400)]
    public string? NameAr { get; set; }            // اسم السجل (full commercial name)

    [MaxLength(400)]
    public string? NameEn { get; set; }

    /// <summary>نسبة التوطين المطلوبة — manual overall required Saudi ratio for the registration (0..1).</summary>
    public decimal RequiredPct { get; set; } = 0m;

    // Nitaqat zone thresholds as Saudi-ratio fractions (0..1). pct < LowGreenMin ⇒ Red, etc.
    public decimal LowGreenMin { get; set; } = 0.10m;     // أخضر منخفض
    public decimal MediumGreenMin { get; set; } = 0.15m;  // أخضر متوسط
    public decimal HighGreenMin { get; set; } = 0.20m;    // أخضر مرتفع
    public decimal PlatinumMin { get; set; } = 0.40m;     // بلاتيني

    // Optional per-zone colour overrides (hex); null ⇒ use the CSS default.
    [MaxLength(9)] public string? RedColor { get; set; }
    [MaxLength(9)] public string? LowGreenColor { get; set; }
    [MaxLength(9)] public string? MediumGreenColor { get; set; }
    [MaxLength(9)] public string? HighGreenColor { get; set; }
    [MaxLength(9)] public string? PlatinumColor { get; set; }

    public ICollection<Facility> Facilities { get; set; } = new List<Facility>();
}

/// <summary>Facility — منشأة. Belongs to one Record, holds many employees.</summary>
public class Facility
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = "";         // رقم المنشأة (e.g. "4 - 9043")

    [MaxLength(400)]
    public string? NameAr { get; set; }            // إسم المنشأة

    [MaxLength(150)]
    public string? Location { get; set; }          // الشركة / الموقع (e.g. Dammam)

    public int RecordId { get; set; }
    public Record? Record { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>
/// A weekly version of the workforce. Each import creates one Snapshot; every employee belongs to
/// exactly one. Comparing snapshots over time is how Saudization progress is monitored.
/// </summary>
public class Snapshot
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Label { get; set; } = "";        // e.g. "2026-W31" or "Week of 1 Aug"

    /// <summary>The week this version represents (stored as the week's date).</summary>
    public DateTime WeekDate { get; set; }

    [MaxLength(400)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(200)]
    public string? CreatedByUser { get; set; }

    /// <summary>The version shown by default across the app. Exactly one snapshot is the default (the most
    /// recently uploaded, unless changed on the Versions page). When none is flagged the app falls back to
    /// the most recent by week.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Soft delete: a deleted version is hidden everywhere (global query filter) but can be
    /// restored from the Versions page until it is purged.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(200)]
    public string? DeletedByUser { get; set; }

    /// <summary>SHA-256 of the version's employee rows (see SnapshotComparer) — detects re-uploads of identical data.</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>A single worker in a facility, as recorded in one weekly snapshot.</summary>
public class Employee
{
    public int Id { get; set; }

    public int SnapshotId { get; set; }            // the weekly version this row belongs to
    public Snapshot? Snapshot { get; set; }

    [MaxLength(60)]
    public string? WorkerNo { get; set; }          // رقم العامل

    [Required, MaxLength(300)]
    public string Name { get; set; } = "";         // اسم العامل

    public int? NationalityId { get; set; }
    public Nationality? Nationality { get; set; }

    [MaxLength(150)]
    public string? Location { get; set; }          // الشركة

    public int FacilityId { get; set; }
    public Facility? Facility { get; set; }

    [MaxLength(60)]
    public string? BorderNo { get; set; }          // رقم الحدود

    [MaxLength(60)]
    public string? IqamaNo { get; set; }           // الإقامة - البطاقة

    public int? OccupationId { get; set; }         // resolved via دليل المهن (may be null = unmapped)
    public Occupation? Occupation { get; set; }

    [MaxLength(300)]
    public string? OccupationRaw { get; set; }     // المهنة (as typed/imported)

    [MaxLength(40)]
    public string? IqamaExpiry { get; set; }       // تاريخ انتهاء الاقامة (Hijri text)

    [MaxLength(40)]
    public string? EntryDate { get; set; }         // تاريخ دخول المملكة (Hijri text)

    [MaxLength(100)]
    public string? WorkerType { get; set; }        // نوع العامل

    // ---- Saudization weighting, resolved per snapshot from the special/disability sheets (§4, §5) ----

    /// <summary>الفئة الخاصة — counts as Saudi in BOTH general and occupation % even if the nationality isn't Saudi.</summary>
    public bool CountsAsSaudiSpecial { get; set; }

    [MaxLength(120)]
    public string? SpecialCategory { get; set; }   // e.g. ابن مواطنة / أم مواطن

    /// <summary>يُحتسب بعدد — general-% weight for a disabled Saudi (1 = normal). Never applied to occupation %.</summary>
    public int DisabilityCountAs { get; set; } = 1;
}

/// <summary>ذوي الاحتياجات الخاصة — a disabled Saudi counts as CountAs heads in Nitaqat.</summary>
public class DisabilityRecord
{
    public int Id { get; set; }

    [MaxLength(60)]
    public string? IdNumber { get; set; }          // رقم الهوية

    [MaxLength(300)]
    public string? Name { get; set; }              // الاسم

    public int CountAs { get; set; } = 4;          // ليُحتسب بعدد
}

/// <summary>الفئة الخاصة — special-case employees.</summary>
public class SpecialCase
{
    public int Id { get; set; }

    [MaxLength(60)]
    public string? IdNumber { get; set; }          // رقم الهوية

    [MaxLength(300)]
    public string? Name { get; set; }              // الاسم

    [MaxLength(200)]
    public string? Category { get; set; }          // الفئة الخاصة

    [MaxLength(300)]
    public string? Occupation { get; set; }        // المهنة
}

/// <summary>Single-row application settings (Id is always 1).</summary>
public class Settings
{
    public int Id { get; set; } = 1;

    /// <summary>Cap on the disabled weighting bonus, as a fraction of the base Saudi count (§5, default 10%).</summary>
    public decimal DisabilityCapPct { get; set; } = 0.10m;

    /// <summary>Light-green band width above the required % before a cell turns high-green (§7, default 5pp).</summary>
    public decimal LightGreenBand { get; set; } = 0.05m;

    /// <summary>Default "يُحتسب بعدد" weight applied at IMPORT time when a disability row leaves the count blank
    /// (Nitaqat default = 4). Applies to the next upload only — it does not retroactively change stored employees.</summary>
    public int DisabilityDefaultWeight { get; set; } = 4;

    /// <summary>Show the Saudi / non-Saudi / total column headers as icons (true) or as plain text (false).</summary>
    public bool ShowNationalityIcons { get; set; } = true;
}

/// <summary>A saved "what-if" scenario for the Saudization workbench — edited aggregate Saudi/non-Saudi
/// counts per (category × registration), tied to a version. Does not touch real employee snapshots.</summary>
public class SaudizationPlan
{
    public int Id { get; set; }

    [Required, MaxLength(160)]
    public string Label { get; set; } = "";

    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(200)]
    public string? CreatedByUser { get; set; }

    public ICollection<SaudizationPlanCell> Cells { get; set; } = new List<SaudizationPlanCell>();
}

/// <summary>One edited cell of a <see cref="SaudizationPlan"/>. CategoryId null = the "Unclassified" row.</summary>
public class SaudizationPlanCell
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int RecordId { get; set; }
    public int? CategoryId { get; set; }
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
}

/// <summary>Immutable change history — one row per changed field. Satisfies "hold changes in the records".</summary>
public class AuditLog
{
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string EntityName { get; set; } = "";

    [MaxLength(60)]
    public string? EntityId { get; set; }

    [Required, MaxLength(10)]
    public string Action { get; set; } = "";       // Insert / Update / Delete

    [MaxLength(100)]
    public string? FieldName { get; set; }

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    [MaxLength(200)]
    public string? ChangedByUser { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
