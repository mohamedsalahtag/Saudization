# Saudization / Nitaqat (نطاقات) Module

A bilingual (Arabic + English) ASP.NET Core 10 MVC web app over SQL Server that replaces the manual
`dashboard.xlsx` workbook. It stores **records → facilities → employees**, **tracks every change**,
and renders the workbook's `Nationalization` sheet as a **live, dynamic dashboard**.

## What it does
- **Dashboard** (landing) — an overview: KPI tiles (overall Saudization, headcount, registrations in
  red, occupations below target, special/disability counts), **Saudization-by-registration** bars, a
  **% over time** trend, a **Findings & issues** list (red registrations, occupations short of target,
  unmapped occupations, registrations missing a required %), and **transfer suggestions** — each linking
  to the relevant detail.
- **Saudization** — the detailed Nationalization pivot recomputed from the database: per
  commercial-registration × Saudization-category Saudi/non-Saudi counts, current %, required %, per-cell
  compliance colour, red-cell replace/hire actions, and each registration's **Nitaqat zone** (Red / Low /
  Medium / High Green / Platinum). Two views: *Current state* and *Improvement plan*. Filter by
  registration and **by version**.
- **Versions (weekly snapshots)** — every Excel import is saved as a dated **version** (a week). The
  dashboard shows a selected version, and **Versions → Compare** charts each registration's Saudization
  % across versions over time, with the change (in percentage points) per registration — this is how
  you monitor progress week to week.
- **Records / Facilities / Employees** — bilingual forms to add and edit each level (employees are
  scoped to a version).
- **Import Excel** — upload a workbook, tag it with a **version label + week date**; reference sheets are
  merged (shared across versions) and all employees load into that version. Choose a **sheet rule** —
  *one sheet per facility* (named by facility number) or *one sheet per registration/company* (named by
  commercial registration; each employee is placed into its facility via the رقم المنشأة column). A
  **Download template** button produces a ready-to-fill workbook matching the chosen rule, pre-filled with
  the current data — edit and re-upload as the new week.
- **Occupations** — review queue for occupations that appear on employees but aren't in the guide
  (blue-collar roles like drivers/loaders); map them to a category and back-fill employees.
- **Change history** — full audit trail (who / when / field / old → new) for every write.
- **Special categories & disabled weighting** (spec §4/§5) — the الفئة الخاصة and ذوي الاحتياجات الخاصة
  sheets are matched to employees by national ID. Special categories count as Saudi in **both** the
  general and occupation %; disabled `يُحتسب بعدد` weight applies to the **general %only**, capped at a
  configurable share (default 10%) of the registration's Saudis.
- **Five Nitaqat zones** (Red / Low / Medium / High Green / Platinum), thresholds **and colors editable
  per registration**; each registration also has a manual required %.
- **Per-occupation cell colouring** vs the required % (red / light-green / high-green), and for red cells
  the **Replace** (إحلال) and **Hire** (توظيف) counts needed to reach compliance.
- **Transfer opportunities** (§8) — suggested one-employee moves between registrations (exploiting the
  headcount floor) with before→after impact on both.
- **What-if simulator** (§9) — an ephemeral sandbox with two modes: **by registration** (general %/zone)
  and **by job** (a category's occupation % per registration, with a **Fill red → green** optimizer that
  computes the Saudis needed). Edits and transfers update live; it never writes to the database.
- **Settings** — the disability cap and the light-green band width.

## Run it locally (dev)
```
cd src/Saudization.Web
dotnet run
```
On first run it creates a local **SQLite** file (`saudization.db`) and seeds it from `../../dashboard.xlsx`.
Open the URL shown (e.g. http://localhost:5099). Switch language with the button top-corner.

## Point at the company SQL Server (production)
The live database is **`Saudization` on `KSAJEDSVSQL003`**, using **Windows/Integrated auth**
(SQL logins are disabled on that server — the app connects as the signed-in domain user).

On a dev machine the connection string is kept in **user-secrets** (never in `appsettings.json`):
```
dotnet user-secrets set "Database:Provider" "SqlServer"
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=KSAJEDSVSQL003;Database=Saudization;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
```
For a server/IIS deploy, put the same two values in the server's `appsettings.Production.json` or
environment variables. Create/upgrade the schema with:
```
dotnet ef database update
```
(The `Saudization` DB and `InitialCreate` migration have already been applied to KSAJEDSVSQL003.)
On SqlServer the app runs EF **migrations** (`Migrations/…_InitialCreate`); on SQLite it uses
`EnsureCreated`. With `Auth:UseWindows=true` the app authenticates via Windows/AD and stamps the
real employee into the audit trail.

## Deploy to IIS
1. Install the **ASP.NET Core 10 Hosting Bundle** on the server.
2. `dotnet publish -c Release`, copy the output to the site folder.
3. Create an IIS site/app pool ("No Managed Code"); enable **Windows Authentication** for the site.
4. Set the SQL Server connection string + `Auth:UseWindows=true` (appsettings or env vars).

## Data model
`Record` (السجل التجاري, holds zone thresholds) → `Facility` (منشأة) → `Employee`.
Reference: `SaudizationCategory` (فئات التوطين), `Occupation` (دليل المهن), `Nationality` (IsSaudi flag).
`AuditLog` holds change history. Employee category is derived: occupation → guide → category;
unmatched (blue-collar) employees fall into an **Unclassified** row but still count in registration
totals and zones — exactly as the workbook's Grand Total does.

## Notes / follow-ups
- The bundled `dashboard.xlsx` pivot cache (`Nationalization` sheet) was built from a larger snapshot
  than the facility sheets currently in the file, so its printed totals (2036) differ slightly from
  the live recomputation (1990 employees, 519 Saudi). The app is the source of truth going forward.
- Zone thresholds are pre-filled with sensible defaults and are **editable per record**; confirm the
  official band values with HR.
- `Microsoft.AspNetCore.Authentication.Negotiate` 10.0.0 has an advisory (NU1903). It is only loaded
  when `Auth:UseWindows=true`; update to the latest patched 10.0.x before production.
