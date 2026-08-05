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

    public int OrgDepartmentId { get; set; }        // Division is Department.Division
    public OrgDepartment? Department { get; set; }

    public int? OrgJobId { get; set; }
    public OrgJob? Job { get; set; }
}
