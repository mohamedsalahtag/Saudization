$ts='C:\Program Files\Tailscale\tailscale.exe'
$out='C:\Saudization\Saudization\_server-setup\ts-move.log'
"=== BEFORE ===" | Out-File $out
& $ts funnel status 2>&1 | Add-Content $out
"=== reset funnel (clears :443) ===" | Add-Content $out
& $ts funnel reset 2>&1 | Add-Content $out
Start-Sleep 2
"=== set funnel on :8443 -> 127.0.0.1:5000 ===" | Add-Content $out
& $ts funnel --bg --https=8443 http://127.0.0.1:5000 --yes 2>&1 | Add-Content $out
Start-Sleep 3
"=== AFTER (funnel status) ===" | Add-Content $out
& $ts funnel status 2>&1 | Add-Content $out
"=== AFTER (serve json) ===" | Add-Content $out
& $ts serve status --json 2>&1 | Add-Content $out
"=== is anything still listening on 443? (want: only IIS/http.sys) ===" | Add-Content $out
(Get-NetTCPConnection -State Listen -LocalPort 443 -ErrorAction SilentlyContinue | Select-Object LocalAddress,OwningProcess | Out-String) | Add-Content $out
"TS-MOVE-DONE" | Add-Content $out
