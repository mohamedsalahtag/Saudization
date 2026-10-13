using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Saudization.Web.Models;

namespace Saudization.Web.Data;

/// <summary>
/// Captures every insert/update/delete on domain entities into <see cref="AuditLog"/>,
/// stamping the signed-in user. This is the "hold changes in the records" requirement.
/// AuditLog itself is never audited (would recurse).
///  • Updates and deletes are written in the same save (their keys are already known).
///  • Inserts are written right after the save, so EntityId holds the real database id (not EF's temporary one).
///  • Bulk rows (employees of an upload, org-chart rows, reference lists) are summarised as one row per
///    entity type and version instead of one row each — an upload used to add ~2,000 audit rows.
///  • Values are stored culture-invariant ("0.4655", not "0٫4655").
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> BulkTypes = new()
    {
        nameof(Employee), nameof(OrgEmployee), nameof(OrgDepartment), nameof(OrgJob), nameof(OrgDivision),
        nameof(DisabilityRecord), nameof(SpecialCase), nameof(SaudizationPlanCell)
    };
    // properties whose changes are noise, not business changes
    private static readonly HashSet<(string, string)> IgnoredProps = new()
    {
        (nameof(AppUser), nameof(AppUser.LastLogin)),
        (nameof(Snapshot), nameof(Snapshot.ContentHash))   // computed fingerprint, not a user change
    };

    private readonly IHttpContextAccessor _http;
    private List<Func<IEnumerable<AuditLog>>>? _pendingInserts;   // built after the save, when ids are real
    private bool _writingPending;

    public AuditSaveChangesInterceptor(IHttpContextAccessor http) => _http = http;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null && !_writingPending) AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null && !_writingPending) AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        if (eventData.Context is not null && TakePending(eventData.Context))
        {
            try { await eventData.Context.SaveChangesAsync(ct); } finally { _writingPending = false; }
        }
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null && TakePending(eventData.Context))
        {
            try { eventData.Context.SaveChanges(); } finally { _writingPending = false; }
        }
        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) { _pendingInserts = null; base.SaveChangesFailed(eventData); }
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    { _pendingInserts = null; return base.SaveChangesFailedAsync(eventData, ct); }

    /// <summary>Adds the post-save insert rows to the context; true when there is something to save.</summary>
    private bool TakePending(DbContext ctx)
    {
        if (_writingPending || _pendingInserts is not { Count: > 0 } pending) return false;
        _pendingInserts = null;
        ctx.Set<AuditLog>().AddRange(pending.SelectMany(f => f()));
        _writingPending = true;
        return true;
    }

    private void AddAuditEntries(DbContext ctx)
    {
        var user = _http.HttpContext?.User?.Identity?.Name
                   ?? _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
                   ?? "system";
        var now = DateTime.UtcNow;

        var logs = new List<AuditLog>();
        var pending = new List<Func<IEnumerable<AuditLog>>>();
        var bulkAdded = new Dictionary<string, List<EntityEntry>>();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var name = entry.Metadata.ClrType.Name;
            var key = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            string? KeyOf(EntityEntry e) => key is null ? null : Fmt(e.CurrentValues[key.Name]);

            switch (entry.State)
            {
                case EntityState.Added when BulkTypes.Contains(name):
                    (bulkAdded.TryGetValue(name, out var l) ? l : bulkAdded[name] = new()).Add(entry);
                    break;
                case EntityState.Added:
                    var e0 = entry;
                    pending.Add(() => new[] { Row(name, KeyOf(e0), "Insert", null, null, null, user, now) });
                    break;
                case EntityState.Deleted:
                    logs.Add(Row(name, KeyOf(entry), "Delete", null, null, null, user, now));
                    break;
                case EntityState.Modified:
                    foreach (PropertyEntry p in entry.Properties)
                    {
                        if (!p.IsModified || IgnoredProps.Contains((name, p.Metadata.Name))) continue;
                        var oldV = Fmt(p.OriginalValue);
                        var newV = Fmt(p.CurrentValue);
                        if (oldV == newV) continue;
                        logs.Add(Row(name, KeyOf(entry), "Update", p.Metadata.Name, oldV, newV, user, now));
                    }
                    break;
            }
        }

        // one summary row per bulk type, grouped by version where the type has one (resolved after the save)
        foreach (var (name, entries) in bulkAdded)
        {
            var parentProp = entries[0].Metadata.FindProperty("SnapshotId") ?? entries[0].Metadata.FindProperty("OrgVersionId");
            if (parentProp is null)
            {
                int n = entries.Count;
                pending.Add(() => new[] { Row(name, null, "Insert", "(bulk)", null, $"{n} rows added", user, now) });
                continue;
            }
            var list = entries; var prop = parentProp.Name;
            pending.Add(() => BulkSummary(name, prop, list, user, now));
        }

        if (logs.Count > 0) ctx.Set<AuditLog>().AddRange(logs);
        if (pending.Count > 0) (_pendingInserts ??= new()).AddRange(pending);
    }

    private static List<AuditLog> BulkSummary(string name, string parentProp, List<EntityEntry> entries, string user, DateTime now) =>
        entries.GroupBy(e => Fmt(e.CurrentValues[parentProp]))
               .Select(g => Row(name, g.Key, "Insert", "(bulk)", null, $"{g.Count()} rows added to {parentProp.Replace("Id", "")} {g.Key}", user, now))
               .ToList();

    private static string? Fmt(object? v) => v switch
    {
        null => null,
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };

    private static AuditLog Row(string entity, string? id, string action, string? field,
        string? oldV, string? newV, string user, DateTime now) => new()
    {
        EntityName = entity,
        EntityId = id,
        Action = action,
        FieldName = field,
        OldValue = oldV,
        NewValue = newV,
        ChangedByUser = user,
        ChangedAtUtc = now
    };
}
