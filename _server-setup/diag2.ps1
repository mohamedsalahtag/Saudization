$out='C:\Saudization\Saudization\_server-setup\diag2.log'
Import-Module WebAdministration
"state before: $((Get-Website 'HelpDeskProxy').State)" | Out-File $out
try { Start-Website 'HelpDeskProxy' -ErrorAction Stop; "Start-Website returned ok" | Add-Content $out } catch { "start EXCEPTION: $($_.Exception.Message)" | Add-Content $out }
Start-Sleep 3
"state after: $((Get-Website 'HelpDeskProxy').State)" | Add-Content $out
"--- recent System events (IIS/HTTP/WAS/W3SVC), last 25 min ---" | Add-Content $out
(Get-EventLog -LogName System -After (Get-Date).AddMinutes(-25) -ErrorAction SilentlyContinue | Where-Object { $_.Source -match 'W3SVC|Http|WAS|IIS' } | Select-Object TimeGenerated,Source,EventID,Message | Format-List | Out-String) | Add-Content $out
"--- listeners on 443 now ---" | Add-Content $out
(Get-NetTCPConnection -State Listen -LocalPort 443 -ErrorAction SilentlyContinue | Select-Object LocalAddress,OwningProcess | Out-String) | Add-Content $out
"--- process holding 443 (16660) ---" | Add-Content $out
(Get-Process -Id 16660 -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path | Out-String) | Add-Content $out
"--- http sslcert bindings ---" | Add-Content $out
(netsh http show sslcert 2>&1 | Select-String 'IP:port|Hostname:port|Certificate Hash|Application ID' | Out-String) | Add-Content $out
"DIAG2-DONE" | Add-Content $out
