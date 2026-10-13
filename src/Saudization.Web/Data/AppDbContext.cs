using Microsoft.EntityFrameworkCore;
using Saudization.Web.Models;

namespace Saudization.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SaudizationCategory> Categories => Set<SaudizationCategory>();
    public DbSet<Occupation> Occupations => Set<Occupation>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<Record> Records => Set<Record>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<DisabilityRecord> DisabilityRecords => Set<DisabilityRecord>();
    public DbSet<SpecialCase> SpecialCases => Set<SpecialCase>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SaudizationPlan> SaudizationPlans => Set<SaudizationPlan>();
    public DbSet<SaudizationPlanCell> SaudizationPlanCells => Set<SaudizationPlanCell>();

    // Login whitelist (user-management pack — login-gate only)
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    // Org restructuring workbench
    public DbSet<OrgVersion> OrgVersions => Set<OrgVersion>();
    public DbSet<OrgDivision> OrgDivisions => Set<OrgDivision>();
    public DbSet<OrgDepartment> OrgDepartments => Set<OrgDepartment>();
    public DbSet<OrgJob> OrgJobs => Set<OrgJob>();
    public DbSet<OrgEmployee> OrgEmployees => Set<OrgEmployee>();
    public DbSet<DepartmentHead> DepartmentHeads => Set<DepartmentHead>();
    public DbSet<OrgFindingAccept> OrgFindingAccepts => Set<OrgFindingAccept>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Record>().HasIndex(r => r.RegistrationLabel).IsUnique();
        b.Entity<Facility>().HasIndex(f => f.Code).IsUnique();
        b.Entity<Nationality>().HasIndex(n => n.NameAr).IsUnique();
        b.Entity<SaudizationCategory>().HasIndex(c => c.NameAr).IsUnique();
        b.Entity<Occupation>().HasIndex(o => o.NameAr);
        b.Entity<Employee>().HasIndex(e => e.WorkerNo);

        // Restrict, not Cascade: deleting a registration or facility must never silently erase the
        // employees of every weekly version. The controllers refuse the delete while employees exist.
        b.Entity<Facility>()
            .HasOne(f => f.Record).WithMany(r => r.Facilities)
            .HasForeignKey(f => f.RecordId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Employee>()
            .HasOne(e => e.Facility).WithMany(f => f.Employees)
            .HasForeignKey(e => e.FacilityId).OnDelete(DeleteBehavior.Restrict);

        // Soft-deleted versions (and their employees) are invisible unless a query calls IgnoreQueryFilters().
        b.Entity<Snapshot>().HasQueryFilter(s => s.DeletedAtUtc == null);
        b.Entity<Employee>().HasQueryFilter(e => e.Snapshot!.DeletedAtUtc == null);
        b.Entity<SaudizationPlan>().HasQueryFilter(p => p.Snapshot!.DeletedAtUtc == null);

        b.Entity<Employee>()
            .HasOne(e => e.Snapshot).WithMany(s => s.Employees)
            .HasForeignKey(e => e.SnapshotId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Employee>().HasIndex(e => e.SnapshotId);
        b.Entity<Snapshot>().HasIndex(s => s.WeekDate);

        b.Entity<Employee>()
            .HasOne(e => e.Occupation).WithMany()
            .HasForeignKey(e => e.OccupationId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Employee>()
            .HasOne(e => e.Nationality).WithMany(n => n.Employees)
            .HasForeignKey(e => e.NationalityId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Occupation>()
            .HasOne(o => o.Category).WithMany(c => c.Occupations)
            .HasForeignKey(o => o.CategoryId).OnDelete(DeleteBehavior.SetNull);

        // Money precision (ignored by SQLite, honoured by SQL Server)
        b.Entity<SaudizationCategory>().Property(c => c.MinWage).HasColumnType("decimal(10,2)");
        foreach (var p in new[] { nameof(SaudizationCategory.CurrentRequiredPct), nameof(SaudizationCategory.FutureRequiredPct) })
            b.Entity<SaudizationCategory>().Property(p).HasColumnType("decimal(5,4)");
        foreach (var p in new[] { nameof(Record.RequiredPct), nameof(Record.LowGreenMin), nameof(Record.MediumGreenMin),
            nameof(Record.HighGreenMin), nameof(Record.PlatinumMin) })
            b.Entity<Record>().Property(p).HasColumnType("decimal(5,4)");
        foreach (var p in new[] { nameof(Models.Settings.DisabilityCapPct), nameof(Models.Settings.LightGreenBand) })
            b.Entity<Models.Settings>().Property(p).HasColumnType("decimal(5,4)");
        b.Entity<Models.Settings>().Property(s => s.Id).ValueGeneratedNever();   // fixed singleton row (Id = 1)

        b.Entity<Employee>().HasIndex(e => e.IqamaNo);   // match special/disability rows by ID

        b.Entity<AuditLog>().HasIndex(a => new { a.EntityName, a.EntityId });
        b.Entity<AuditLog>().HasIndex(a => a.ChangedAtUtc);          // audit page by date, retention clean-up
        b.Entity<Employee>().HasIndex(e => e.OccupationRaw);         // occupation mapping / guide re-linking

        b.Entity<AppUser>().HasIndex(u => u.AdUsername).IsUnique();

        // ---- Saudization what-if scenarios ----
        b.Entity<SaudizationPlan>()
            .HasOne(p => p.Snapshot).WithMany()
            .HasForeignKey(p => p.SnapshotId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SaudizationPlan>()
            .HasMany(p => p.Cells).WithOne()
            .HasForeignKey(c => c.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SaudizationPlanCell>().HasIndex(c => c.PlanId);

        // ---- Org restructuring: one graph per version. All structural FKs are NoAction to avoid
        // SQL Server multiple-cascade-path errors; version deletion runs ordered ExecuteDelete instead. ----
        foreach (var t in new[] { typeof(OrgDivision), typeof(OrgDepartment), typeof(OrgJob), typeof(OrgEmployee) })
            b.Entity(t).HasIndex(nameof(OrgDivision.OrgVersionId));
        b.Entity<OrgEmployee>().HasIndex(e => new { e.OrgVersionId, e.Code });
        b.Entity<DepartmentHead>().HasIndex(h => new { h.OrgVersionId, h.Division, h.Department }).IsUnique();
        b.Entity<OrgFindingAccept>().HasIndex(a => new { a.FindingId, a.RowKey }).IsUnique();

        b.Entity<OrgVersion>().HasMany(v => v.Divisions).WithOne().HasForeignKey(x => x.OrgVersionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgVersion>().HasMany(v => v.Departments).WithOne().HasForeignKey(x => x.OrgVersionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgVersion>().HasMany(v => v.Jobs).WithOne().HasForeignKey(x => x.OrgVersionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgVersion>().HasMany(v => v.Employees).WithOne().HasForeignKey(x => x.OrgVersionId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<OrgDepartment>().HasOne(d => d.Division).WithMany()
            .HasForeignKey(d => d.OrgDivisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgJob>().HasOne(j => j.Department).WithMany()
            .HasForeignKey(j => j.OrgDepartmentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgEmployee>().HasOne(e => e.Department).WithMany()
            .HasForeignKey(e => e.OrgDepartmentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<OrgEmployee>().HasOne(e => e.Job).WithMany()
            .HasForeignKey(e => e.OrgJobId).OnDelete(DeleteBehavior.NoAction);
    }
}
