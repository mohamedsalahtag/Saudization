// PresenceSweepService - keeps Users.IsOnline honest (real-time-ish presence).
// Register with:  builder.Services.AddHostedService<PresenceSweepService>();
//
// Presence model (three moving parts):
//   1. Login sets IsOnline=1               (AccountController -> SetUserOnlineAsync).
//   2. While a page is open the browser POSTs a heartbeat ~every 60s to a tiny
//      authenticated endpoint that calls UpdateLastSeenAsync (bumps LastSeen).
//      A minimal endpoint is all you need, e.g. on Home/Account:
//          [Authorize, HttpPost]
//          public async Task<IActionResult> Ping() {
//              await _db.UpdateLastSeenAsync(GetUserId()); return NoContent();
//          }
//      and in your layout, for signed-in users:
//          setInterval(() => fetch('/Account/Ping', {method:'POST'}), 60000);
//   3. This sweep flips IsOnline=0 for anyone whose LastSeen is older than the
//      window — so closing the tab, sleeping, or a crash drops them offline
//      automatically instead of leaving a stale "online" flag forever.
//
// (Explicit Logout still clears IsOnline immediately; the sweep covers everyone
//  who leaves WITHOUT logging out.)

using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Services;

public class PresenceSweepService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private const int WindowSeconds = 150;                         // offline after ~2.5 min of silence
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public PresenceSweepService(IServiceProvider sp) => _sp = sp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();   // hosted service is singleton; DbService is scoped
                var db = scope.ServiceProvider.GetRequiredService<IDbService>();
                await db.MarkStalePresenceOfflineAsync(WindowSeconds);
            }
            catch { /* transient DB hiccup — try again next tick */ }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
