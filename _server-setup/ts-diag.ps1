$ts='C:\Program Files\Tailscale\tailscale.exe'
$out='C:\Saudization\Saudization\_server-setup\ts.log'
"=== version ===" | Out-File $out
& $ts version 2>&1 | Add-Content $out
"=== status (self) ===" | Add-Content $out
& $ts status --self=true --peers=false 2>&1 | Add-Content $out
"=== serve status (JSON) ===" | Add-Content $out
& $ts serve status 2>&1 | Add-Content $out
"=== funnel status ===" | Add-Content $out
& $ts funnel status 2>&1 | Add-Content $out
"=== serve status --json ===" | Add-Content $out
& $ts serve status --json 2>&1 | Add-Content $out
"TS-DONE" | Add-Content $out
