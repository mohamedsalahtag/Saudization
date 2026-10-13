# ============================================================================
#  Saudization.Web — one-command deploy, run from the developer workstation (N:\ mapped).
#    1. dotnet publish (Release) to a temp folder
#    2. COPY_ONLY backup of the Saudization database (runs here, not on the web server:
#       the remote session cannot pass Windows credentials on to SQL — the "double hop")
#    3. keep a copy of the current live release in _releases\<timestamp> (rollback = copy it back to _stage and redeploy)
#    4. mirror the new publish into _stage\publish
#    5. run deploy-run.ps1 on the web server and show its result
#  Usage:  powershell -File N:\Saudization\_server-setup\deploy.ps1 [-SkipBackup]
# ============================================================================
param([switch]$SkipBackup)
$ErrorActionPreference = 'Stop'

$root     = 'N:\Saudization'
$project  = Join-Path $root 'src\Saudization.Web'
$stage    = Join-Path $root '_stage\publish'
$live     = Join-Path $root 'publish'
$releases = Join-Path $root '_releases'
$server   = 'KSAJEDSVAIP001'
$sql      = 'KSAJEDSVSQL003'
$db       = 'Saudization'
$ts       = Get-Date -Format 'yyyyMMdd_HHmmss'
$out      = Join-Path $env:TEMP "saudization-publish-$ts"

Write-Host '== 1/5  Publish =='
dotnet publish $project -c Release -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host '== 2/5  Database backup (COPY_ONLY) =='
if ($SkipBackup) { Write-Host '   skipped (-SkipBackup)' }
else {
    $cn = New-Object System.Data.SqlClient.SqlConnection "Server=$sql;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15"
    $cn.Open()
    try {
        $c = $cn.CreateCommand(); $c.CommandTimeout = 600
        $c.CommandText = "DECLARE @d nvarchar(4000); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @d OUTPUT; SELECT @d"
        $dir = [string]$c.ExecuteScalar()
        $bak = Join-Path $dir "${db}_predeploy_$ts.bak"
        $c.CommandText = "BACKUP DATABASE [$db] TO DISK = @p WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT"
        [void]$c.Parameters.AddWithValue('@p', $bak)
        [void]$c.ExecuteNonQuery()
        Write-Host "   backup written on $sql : $bak"
    } finally { $cn.Close() }
}

Write-Host '== 3/5  Keep current live release =='
$keep = Join-Path $releases $ts
robocopy $live $keep /MIR /XD logs dp-keys uploads /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copy of live release failed (robocopy exit $LASTEXITCODE)" }
Write-Host "   saved to $keep"
Get-ChildItem $releases -Directory | Sort-Object Name -Descending | Select-Object -Skip 5 | Remove-Item -Recurse -Force   # keep the last 5

Write-Host '== 4/5  Stage new build =='
robocopy $out $stage /MIR /XD logs dp-keys uploads /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "staging failed (robocopy exit $LASTEXITCODE)" }

Write-Host "== 5/5  Deploy on $server =="
Invoke-Command -ComputerName $server -FilePath (Join-Path $root '_server-setup\deploy-run.ps1') | Out-Null
$logText = Get-Content (Join-Path $root '_server-setup\deploy-run.log') | Select-String 'pool state|robocopy|GET |EVENT|DEPLOY-RESULT|no application errors'
$logText | ForEach-Object { Write-Host "   $($_.Line.Trim())" }
$same = (Get-FileHash (Join-Path $live 'Saudization.Web.dll')).Hash -eq (Get-FileHash (Join-Path $out 'Saudization.Web.dll')).Hash
Write-Host "   live DLL matches new build: $same"
Remove-Item $out -Recurse -Force
if (-not $same -or -not ($logText -match 'DEPLOY-RESULT: SUCCESS')) { Write-Host "Deploy needs attention. Rollback: robocopy $keep $stage /MIR /XD logs dp-keys uploads, then run deploy-run.ps1 again." -ForegroundColor Yellow }
