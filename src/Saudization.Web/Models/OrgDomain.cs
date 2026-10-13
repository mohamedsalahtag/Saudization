using System.ComponentModel.DataAnnotations;

namespace Saudization.Web.Models;

/// <summary>
/// A full snapshot of the org hierarchy (divisions/departments/jobs/employees) for the restructuring
/// workbench. The first import is the immutable "original"; each saved edit is a new version.
/// </summary>
public class OrgVersion
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Label { get; set; } = "";

    public bool IsOriginal { get; set; }

    [MaxLength(400)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(200)]
    public string? CreatedByUser { get; set; }

    public int? SourceVersionId { get; set; }   // which version this was derived from

    public ICollection<OrgDivision> Divisions { get; set; } = new List<OrgDivision>();
    public ICollection<OrgDepartment> Departments { get; set; } = new List<OrgDepartment>();
    public ICollection<OrgJob> Jobs { get; set; } = new List<OrgJob>();
    public ICollection<OrgEmployee> Employees { get; set; } = new List<OrgEmployee>();
}

public class OrgDivision
{
    public int Id { get; set; }
    public int OrgVersionId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";
}

public class OrgDepartment
{
    public int Id { get; set; }
    public int OrgVersionId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public int OrgDivisionId { get; set; }
    public OrgDivision? Division { get; set; }
}

/// <summary>Optional sub-level below a department. Empty in the current data; populated via the template later.</summary>
public class OrgJob
{
    public int Id { get; set; }
    public int OrgVersionId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public int OrgDepartmentId { get; set; }
    public OrgDepartment? Department { get; set; }
}

/// <summary>Authoritative head-of-department reference (the "HOD" sheet: Division · Department · Direct
/// Manager ID · Name). Global reference data — not tied to a version; matched to chart groups by
/// Division + Department name so the org chart shows the real head instead of a guess.</summary>
public class DepartmentHead
{
    public int Id { get; set; }
    /// <summary>Null = the legacy global reference; otherwise the HOD sheet imported with that org version.</summary>
    public int? OrgVersionId { get; set; }
    [Required, MaxLength(200)] public string Division { get; set; } = "";
    [Required, MaxLength(200)] public string Department { get; set; } = "";
    [MaxLength(60)] public string? HeadCode { get; set; }
    [MaxLength(300)] public string? HeadName { get; set; }
}

/// <summary>A finding row the HR team has accepted as expected (e.g. the COO as their own manager). Global, not
/// per version: it stays hidden from the counts on every later upload until it is undone.</summary>
public class OrgFindingAccept
{
    public int Id { get; set; }
    [Required, MaxLength(60)] public string FindingId { get; set; } = "";
    [Required, MaxLength(160)] public string RowKey { get; set; } = "";
    [MaxLength(300)] public string? RowLabel { get; set; }
    [MaxLength(200)] public string? AcceptedByUser { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
}

public class OrgEmployee
{
    public int Id { get; set; }
    public int OrgVersionId { get; set; }

    [MaxLength(60)]
    public string? Code { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; set; } = "";

    [MaxLength(60)]
    public string? BranchCode { get; set; }

    [MaxLength(150)]
    public string? Site { get; set; }

    [MaxLength(150)]
    public string? Nationality { get; set; }

    [MaxLength(60)]
    public string? DirectManagerCode { get; set; }

    /// <summary>"Direct Manager Name" as written in the HR export — a second source to validate the manager code.</summary>
    [MaxLength(300)]
    public string? DirectManagerName { get; set; }

    /// <summary>HR grade text, e.g. "Grade 12 - Deputy Manager"; the number makes seniority checks exact.</summary>
    [MaxLength(100)]
    public string? Grade { get; set; }

    public int OrgDepartmentId { get; set; }        // Division is Department.Division
    public OrgDepartment? Department { get; set; }

    public int? OrgJobId { get; set; }
    public OrgJob? Job { get; set; }
}
