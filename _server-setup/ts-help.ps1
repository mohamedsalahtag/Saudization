$ts='C:\Program Files\Tailscale\tailscale.exe'
$out='C:\Saudization\Saudization\_server-setup\ts-help.log'
"=== funnel --help ===" | Out-File $out
& $ts funnel --help 2>&1 | Add-Content $out
"=== serve --help ===" | Add-Content $out
& $ts serve --help 2>&1 | Add-Content $out
"TS-HELP-DONE" | Add-Content $out
