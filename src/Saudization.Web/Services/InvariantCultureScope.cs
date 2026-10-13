using System.Globalization;

namespace Saudization.Web.Services;

/// <summary>Runs a block under the invariant culture and restores the request culture afterwards. Used around
/// ClosedXML reads: with the Arabic culture its formula engine produces formulas it can't parse.</summary>
public sealed class InvariantCultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture, _ui = CultureInfo.CurrentUICulture;
    public InvariantCultureScope() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    public void Dispose() { CultureInfo.CurrentCulture = _culture; CultureInfo.CurrentUICulture = _ui; }
}
