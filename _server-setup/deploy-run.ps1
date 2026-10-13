# ============================================================================
#  Saudization.Web — redeploy (runs ON the web server KSAJEDSVAIP001 as SYSTEM).
#  Ships the user-management update: stops the pool, mirrors the staged publish
#  over the live folder, switches IIS auth from Windows -> Anonymous (the app now
#  does its own AD/cookie login), restarts, and probes the login page.
# ============================================================================
$root  = 'C:\Saudization\Saudization'
$live  = Join-Path $root 'publish'
$stage = Join-Path $root '_stage\publish'
$pool  = 'SaudizationPool'
$site  = 'Saudization'
$port  = 8080
$log   = Join-Path $root '_server-setup\deploy-run.log'

Start-Transcript -Path $log -Force | Out-Null
try {
    $ErrorActionPreference = 'Stop'
    Import-Module WebAdministration
    $start = Get-Date
    Write-Host "HOST=$env:COMPUTERNAME USER=$env:USERNAME $(Get-Date -Format s)"

    Write-Host '== 1/6  Stop app pool =='
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool }
    for ($i=0; $i -lt 30; $i++) {
        if ((Get-WebAppPoolState -Name $pool).Value -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 500
    }
    $ps = (Get-WebAppPoolState -Name $pool).Value
    Write-Host "   pool state: $ps"
    if ($ps -ne 'Stopped') { throw "App pool did not stop (state: $ps) - aborting before copying files." }
    Start-Sleep -Seconds 2   # let w3wp release the DLL locks

    Write-Host '== 2/6  Mirror staged publish over live (preserve logs + data-protection keys) =='
    if (-not (Test-Path $stage)) { throw "Stage folder missing: $stage" }
    robocopy $stage $live /MIR /XD logs dp-keys uploads /NFL /NDL /NJH /NJS /NP /R:3 /W:2 | Out-Null
    $rc = $LASTEXITCODE
    Write-Host "   robocopy exit: $rc (0-7 = ok)"
    if ($rc -ge 8) { Start-WebAppPool -Name $pool; throw "robocopy failed (exit $rc) - live folder may be incomplete; restore from _releases if the site misbehaves." }

    Write-Host '== 3/6  Ensure logs + dp-keys folders writable by the app pool =='
    $logs = Join-Path $live 'logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    & icacls $logs /grant ("IIS AppPool\{0}:(OI)(CI)M" -f $pool) | Out-Null
    # Data-protection keys live here and MUST survive deploys/recycles (kept out of the mirror above),
    # so auth cookies stay valid and users are not logged out on every deploy.
    $keys = Join-Path $live 'dp-keys'
    New-Item -ItemType Directory -Force -Path $keys | Out-Null
    & icacls $keys /grant ("IIS AppPool\{0}:(OI)(CI)M" -f $pool) | Out-Null
    # uploads waiting for the user to confirm the preview (kept out of the mirror, like logs and dp-keys)
    $uploads = Join-Path $live 'uploads'
    New-Item -ItemType Directory -Force -Path $uploads | Out-Null
    & icacls $uploads /grant ("IIS AppPool\{0}:(OI)(CI)M" -f $pool) | Out-Null

    Write-Host '== 3b/6  Keep the app pool alive (no idle shutdown, no periodic recycle) =='
    Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout       -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$pool" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
    & "$env:windir\system32\inetsrv\appcmd.exe" set apppool $pool /startMode:AlwaysRunning | Out-Null
    $it = (Get-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout).Value
    $sm = & "$env:windir\system32\inetsrv\appcmd.exe" list apppool $pool /text:startMode
    Write-Host "   idleTimeout=$it  startMode=$sm"

    Write-Host '== 4/6  IIS auth: Anonymous ON, Windows OFF (app handles login now) =='
    Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $site -Filter '/system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
    Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $site -Filter '/system.webServer/security/authentication/windowsAuthentication'  -Name enabled -Value $false
    $anon = (Get-WebConfigurationProperty -PSPath 'IIS:\' -Location $site -Filter '/system.webServer/security/authentication/anonymousAuthentication' -Name enabled).Value
    $win  = (Get-WebConfigurationProperty -PSPath 'IIS:\' -Location $site -Filter '/system.webServer/security/authentication/windowsAuthentication'  -Name enabled).Value
    Write-Host "   anonymous=$anon  windows=$win"

    Write-Host '== 5/6  Start app pool =='
    Start-WebAppPool -Name $pool
    if ((Get-Website -Name $site).State -ne 'Started') { Start-Website -Name $site }
    Write-Host "   pool state: $((Get-WebAppPoolState -Name $pool).Value)"

    Write-Host '== 6/6  Probe (app runs EF migration on first hit; retry) =='
    $login = 0; $rootc = 0
    for ($i=0; $i -lt 40; $i++) {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$port/Account/Login" -UseBasicParsing -TimeoutSec 10 -MaximumRedirection 0 -ErrorAction Stop
            $login = [int]$r.StatusCode
        } catch { $login = [int]($_.Exception.Response.StatusCode.value__) }
        if ($login -eq 200) { break }
        Start-Sleep -Seconds 2
    }
    try {
        $req = [System.Net.HttpWebRequest]::Create("http://localhost:$port/")
        $req.AllowAutoRedirect = $false; $req.Timeout = 10000
        $resp = $req.GetResponse(); $rootc = [int]$resp.StatusCode; $resp.Close()
    } catch [System.Net.WebException] { if ($_.Exception.Response) { $rootc = [int]$_.Exception.Response.StatusCode } }
    Write-Host "   GET /Account/Login = $login (expect 200)"
    Write-Host "   GET /               = $rootc (expect 302 -> login)"

    Write-Host '== newest stdout log tail =='
    # only logs written by this deploy (stdout logging is normally off), plus app errors from the Event Log
    $sl = Get-ChildItem $logs -Filter 'stdout*.log' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $start } | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($sl) { Get-Content $sl.FullName -Tail 25 | ForEach-Object { Write-Host "   $_" } } else { Write-Host '   (no stdout log from this deploy)' }
    $ev = Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$start; Level=1,2 } -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'ASP.NET|IIS|\.NET Runtime' } | Select-Object -First 5
    if ($ev) { $ev | ForEach-Object { Write-Host "   EVENT $($_.TimeCreated) $($_.ProviderName): $(($_.Message -split "`n")[0])" } } else { Write-Host '   (no application errors in the Event Log since deploy start)' }

    if ($login -eq 200 -and $rootc -eq 302) { Write-Host 'DEPLOY-RESULT: SUCCESS' }
    else { Write-Host 'DEPLOY-RESULT: CHECK (unexpected status codes)' }
}
catch { Write-Host "DEPLOY-RESULT: FAILED: $($_.Exception.Message)" }
finally { Stop-Transcript | Out-Null }
