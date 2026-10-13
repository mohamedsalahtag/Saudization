# ============================================================================
#  Saudization.Web — one-time IIS bring-up (runs ON the web server as SYSTEM).
#  Installs the .NET 10 ASP.NET Core Hosting Bundle + the IIS Windows-Auth
#  feature, then creates the app pool + site and starts it.
#  DB auth = the server's MACHINE ACCOUNT (already granted on SQL) — no password.
# ============================================================================
$here     = 'C:\Saudization\Saudization\_server-setup'
$appPath  = 'C:\Saudization\Saudization\publish'
$poolName = 'SaudizationPool'
$siteName = 'Saudization'
$port     = 8080
$log      = Join-Path $here 'setup-run.log'

Start-Transcript -Path $log -Force | Out-Null
try {
    $ErrorActionPreference = 'Stop'
    Write-Host "HOST=$env:COMPUTERNAME  USER=$env:USERNAME  $(Get-Date -Format s)"

    Write-Host '== 1/6  IIS Windows Authentication feature =='
    try { Import-Module ServerManager -ErrorAction Stop; Install-WindowsFeature Web-Windows-Auth -ErrorAction Stop | Out-Null; Write-Host '   Web-Windows-Auth installed' }
    catch { Write-Host "   Install-WindowsFeature failed ($($_.Exception.Message)); trying DISM"; & dism.exe /online /enable-feature /featurename:IIS-WindowsAuthentication /all /norestart | Out-Null }

    Write-Host '== 2/6  .NET 10 ASP.NET Core Hosting Bundle =='
    $p = Start-Process -FilePath (Join-Path $here 'dotnet-hosting-10.0-win.exe') -ArgumentList '/quiet','/install','/norestart' -Wait -PassThru
    Write-Host "   installer exit code: $($p.ExitCode)"
    Write-Host '   iisreset...'; iisreset | Out-Null
    $rt = & dotnet --list-runtimes | Select-String 'AspNetCore.App 10'
    if (-not $rt) { throw 'ASP.NET Core 10 runtime not present after install.' }
    $rt | ForEach-Object { Write-Host "   $_" }

    Import-Module WebAdministration
    Write-Host '== 3/6  App pool (No Managed Code, ApplicationPoolIdentity=machine acct) =='
    if (Test-Path "IIS:\AppPools\$poolName") { Remove-WebAppPool $poolName }
    New-WebAppPool $poolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$poolName" managedRuntimeVersion ''
    Set-ItemProperty "IIS:\AppPools\$poolName" startMode 'AlwaysRunning'

    Write-Host '== 4/6  Site =='
    if (Get-Website -Name $siteName -ErrorAction SilentlyContinue) { Remove-Website $siteName }
    New-Website -Name $siteName -PhysicalPath $appPath -ApplicationPool $poolName -Port $port | Out-Null

    Write-Host '== 5/6  Windows Auth on (Anonymous off) =='
    Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $siteName -Filter '/system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
    Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $siteName -Filter '/system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false

    Write-Host '== 6/6  Start =='
    Start-WebAppPool $poolName; Start-Website $siteName
    Write-Host "URL: http://$($env:COMPUTERNAME):$port/"
    Write-Host 'SETUP-RESULT: SUCCESS'
}
catch { Write-Host "SETUP-RESULT: FAILED: $($_.Exception.Message)" }
finally { Stop-Transcript | Out-Null }
