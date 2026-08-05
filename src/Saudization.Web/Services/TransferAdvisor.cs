using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Finds Saudization improvement opportunities via employee transfers between registrations (§8),
/// focusing on the MinForCalc threshold: a registration below the floor is exempt for that category,
/// so a Saudi sitting there is "wasted" and a non-Saudi there is "safe".
/// </summary>
public class TransferAdvisor
{
    private readonly NitaqatCalculator _calc;
    public TransferAdvisor(NitaqatCalculator calc) => _calc = calc;

    public async Task<List<TransferSuggestion>> SuggestAsync(int snapshotId, CancellationToken ct = default)
    {
        var vm = await _calc.BuildAsync(DashboardMode.Current, null, snapshotId, ct);
        var recs = vm.Records;
        var outList = new List<TransferSuggestion>();

        foreach (var row in vm.Rows)
        {
            if (row.IsUnclassified || row.RequiredPct <= 0) continue;
            decimal req = row.RequiredPct;

            // per-registration cell snapshot for this category
            var cells = recs.Select(rc =>
            {
                var c = row.ByRecord.TryGetValue(rc.RecordId, out var cell) ? cell : new Cell();
                return new { rc.RecordId, rc.Label, c.Saudi, c.NonSaudi, c.Total };
            }).ToList();

            foreach (var red in cells)
            {
                bool isRed = red.Total >= row.MinForCalc && red.Total > 0 && (decimal)red.Saudi / red.Total < req;
                if (!isRed) continue;

                // Prefer bringing a Saudi from a donor that can spare one (exempt reg, or a surplus reg).
                var donor = cells
                    .Where(d => d.RecordId != red.RecordId && d.Saudi >= 1)
                    .Where(d => d.Total < row.MinForCalc /* exempt: Saudi wasted */
                             || (d.Total > 1 && (decimal)(d.Saudi - 1) / (d.Total - 1) >= req) /* surplus */)
                    .OrderByDescending(d => d.Total < row.MinForCalc ? 1 : 0)
                    .ThenByDescending(d => d.Saudi)
                    .FirstOrDefault();

                if (donor != null)
                {
                    outList.Add(new TransferSuggestion
                    {
                        CategoryName = row.NameAr,
                        BringSaudi = true,
                        FromLabel = donor.Label,
                        ToLabel = red.Label,
                        FromPctBefore = donor.Total > 0 ? (decimal)donor.Saudi / donor.Total : 0,
                        FromPctAfter = donor.Total > 1 ? (decimal)(donor.Saudi - 1) / (donor.Total - 1) : 0,
                        ToPctBefore = (decimal)red.Saudi / red.Total,
                        ToPctAfter = (decimal)(red.Saudi + 1) / (red.Total + 1),
                        Rationale = donor.Total < row.MinForCalc ? "donor_exempt" : "donor_surplus"
                    });
                }
                else
                {
                    // Otherwise move a non-Saudi OUT of the red registration to an exempt one — raises the red %.
                    var exempt = cells.FirstOrDefault(d => d.RecordId != red.RecordId && d.Total < row.MinForCalc);
                    if (red.NonSaudi >= 1 && exempt != null && red.Total > 1)
                    {
                        outList.Add(new TransferSuggestion
                        {
                            CategoryName = row.NameAr,
                            BringSaudi = false,
                            FromLabel = red.Label,
                            ToLabel = exempt.Label,
                            FromPctBefore = (decimal)red.Saudi / red.Total,
                            FromPctAfter = (decimal)red.Saudi / (red.Total - 1),
                            ToPctBefore = exempt.Total > 0 ? (decimal)exempt.Saudi / exempt.Total : 0,
                            ToPctAfter = (decimal)exempt.Saudi / (exempt.Total + 1),
                            Rationale = "offload_nonsaudi"
                        });
                    }
                }
            }
        }

        // most impactful first (biggest gain for the receiving/red registration)
        return outList
            .OrderByDescending(s => (s.BringSaudi ? s.ToPctAfter - s.ToPctBefore : s.FromPctAfter - s.FromPctBefore))
            .Take(100)
            .ToList();
    }
}
