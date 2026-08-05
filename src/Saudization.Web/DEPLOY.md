# Deploying Saudization.Web to IIS

The app hosts **two modules** — the Saudization/Nitaqat dashboard and the Org Restructuring workbench —
over **SQL Server** (`Saudization` on `KSAJEDSVSQL003`), with **Windows Authentication**.

## 1. Server prerequisites (one-time)
- **.NET 10 + ASP.NET Core 10.0.10 Hosting Bundle** (or newer 10.x) installed on the IIS server.
  Download: the "ASP.NET Core Runtime — Hosting Bundle" from the .NET 10 downloads page. After installing,
  run `iisreset`. Confirm with `dotnet --list-runtimes` (needs `Microsoft.AspNetCore.App 10.0.10+`).
- **IIS role features**: *Windows Authentication* and *WebSocket* (optional) enabled.

## 2. Publish (on the build machine)
```
cd src/Saudization.Web
dotnet publish -c Release -o publish
```
This produces `publish/` with `Saudization.Web.dll`, `web.config` (in-process AspNetCoreModuleV2),
`appsettings.json`, `appsettings.Production.json`, and `wwwroot/`. Copy the whole `publish/` folder to the
server, e.g. `C:\inetpub\Saudization`.

## 3. Configure IIS
1. **App pool**: create one (e.g. `SaudizationPool`), **.NET CLR version = No Managed Code**,
   pipeline *Integrated*.
2. **Site/app**: point it at the copied folder; assign the app pool.
3. **Authentication** (site → Authentication): **enable Windows Authentication**, **disable Anonymous**.
   (The app also sets `Auth:UseWindows=true` in `appsettings.Production.json` so it requires an authenticated
   user; the audit trail then records the real signed-in employee.)

## 4. Database access — important
The app connects to SQL Server with **Integrated Security**, so the connection runs as the **app-pool
identity**, *not* the browser user. Give that identity access to the `Saudization` database:
- Set the app pool identity to a **domain service account** (recommended) that can reach `KSAJEDSVSQL003`.
- On SQL Server, add that account as a login and grant it on `Saudization`: **db_datareader, db_datawriter,
  db_ddladmin** (ddladmin lets EF apply future migrations on startup). Or a fixed role your DBA prefers.
- The DB and schema already exist; on startup the app runs any *pending* EF migrations (idempotent) and
  skips seeding because data is present.

Connection string / settings live in `appsettings.Production.json` (no password — Integrated auth). Adjust
the server name there if needed. ASP.NET Core defaults `ASPNETCORE_ENVIRONMENT` to **Production** on IIS,
so that file loads automatically.

## 5. Verify
- Browse the site → the Dashboard overview loads; nav shows Dashboard · Saudization · Records · Versions ·
  Restructure · Occupations · Import · Change history · Settings.
- Make an edit (e.g. Settings) → **Change history** records it under your Windows account.
- Switch language (top corner) → RTL Arabic renders.

## Notes
- **Security**: the `Negotiate` package is on the patched **10.0.10** (advisory cleared). The remaining
  `SQLitePCLRaw` advisory is **dev-only** — SQLite is just the local-dev fallback provider and is never
  loaded in Production (which uses SQL Server). It can be ignored, or the SQLite package removed if you
  never run the app locally without SQL Server.
- **Redeploys**: re-run `dotnet publish` and copy over the folder (stop the app pool first to release file
  locks). View/CSS/JS/`.cshtml` changes require a redeploy (views are precompiled).
- **Logs**: set `stdoutLogEnabled="true"` in `web.config` and create a `logs\` folder to capture startup
  errors during first bring-up, then turn it back off.
