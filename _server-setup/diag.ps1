$out = 'C:\Saudization\Saudization\_server-setup\diag.log'
"=== $(Get-Date -Format s)  diag on $env:COMPUTERNAME ===" | Out-File $out
Import-Module WebAdministration -ErrorAction SilentlyContinue
"--- SITES ---" | Add-Content $out
Get-Website | ForEach-Object { "{0}  state={1}  pool={2}" -f $_.Name, $_.State, $_.applicationPool } | Add-Content $out
"--- APP POOLS ---" | Add-Content $out
Get-ChildItem IIS:\AppPools | ForEach-Object { "{0}  state={1}" -f $_.Name, (Get-WebAppPoolState $_.Name).Value } | Add-Content $out
"--- LISTENERS :5000/:80/:443 ---" | Add-Content $out
(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5000,80,443 } | Select-Object LocalAddress,LocalPort,OwningProcess | Sort-Object LocalPort | Out-String) | Add-Content $out
"--- dotnet.exe processes (backend?) ---" | Add-Content $out
(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Select-Object ProcessId,CommandLine | Out-String) | Add-Content $out
"--- services matching help/desk/kestrel/saud ---" | Add-Content $out
(Get-Service | Where-Object { $_.Name -match 'help|desk|kestrel|saud|w3svc|was' -or $_.DisplayName -match 'help|desk' } | Select-Object Name,Status,StartType | Out-String) | Add-Content $out
"--- URL Rewrite module (rewrite.dll) present? ---" | Add-Content $out
(Test-Path 'C:\Windows\System32\inetsrv\rewrite.dll') | Add-Content $out
"--- backend test http://127.0.0.1:5000/ ---" | Add-Content $out
try { $r=Invoke-WebRequest 'http://127.0.0.1:5000/' -UseBasicParsing -TimeoutSec 12; ("backend HTTP " + $r.StatusCode) | Add-Content $out } catch { ("backend FAILED: " + $_.Exception.Message) | Add-Content $out }
"DIAG-DONE" | Add-Content $out
