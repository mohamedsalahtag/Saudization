using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Saudization.Web.Models;

namespace Saudization.Web.Data;

/// <summary>
/// Captures every insert/update/delete on domain entities into <see cref="AuditLog"/>,
/// stamping the signed-in Windows user. This is the "hold changes in the records" requirement.
/// AuditLog itself is never audited (would recurse).
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _http;

    public AuditSaveChangesInterceptor(IHttpContextAccessor http) => _http = http;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void AddAuditEntries(DbContext ctx)
    {
        var user = _http.HttpContext?.User?.Identity?.Name
                   ?? _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
                   ?? "system";
        var now = DateTime.UtcNow;

        var logs = new List<AuditLog>();
        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var name = entry.Metadata.ClrType.Name;
            var key = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            var id = key is null ? null : entry.CurrentValues[key.Name]?.ToString();

            switch (entry.State)
            {
                case EntityState.Added:
                    logs.Add(Row(name, id, "Insert", null, null, null, user, now));
                    break;
                case EntityState.Deleted:
                    logs.Add(Row(name, id, "Delete", null, null, null, user, now));
                    break;
                case EntityState.Modified:
                    foreach (PropertyEntry p in entry.Properties)
                    {
                        if (!p.IsModified) continue;
                        var oldV = p.OriginalValue?.ToString();
                        var newV = p.CurrentValue?.ToString();
                        if (oldV == newV) continue;
                        logs.Add(Row(name, id, "Update", p.Metadata.Name, oldV, newV, user, now));
                    }
                    break;
            }
        }
        if (logs.Count > 0) ctx.Set<AuditLog>().AddRange(logs);
    }

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
