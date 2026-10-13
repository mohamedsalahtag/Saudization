$ts='C:\Program Files\Tailscale\tailscale.exe'
$out='C:\Saudization\Saudization\_server-setup\ts-move2.log'
"start" | Out-File $out
& $ts funnel reset 2>&1 | Add-Content $out
$attempts = @(
  @('--bg','--https=8443','--yes','http://127.0.0.1:5000'),
  @('--bg','--https=8443','http://127.0.0.1:5000'),
  @('--https=8443','--bg','--yes','http://localhost:5000')
)
$done=$false
foreach($a in $attempts){
  if($done){ break }
  ("--- try: funnel " + ($a -join ' ')) | Add-Content $out
  (& $ts funnel @a 2>&1) | Add-Content $out
  Start-Sleep 2
  $st = ((& $ts funnel status 2>&1) -join "`n")
  if($st -match '8443'){ "=> SUCCESS on 8443" | Add-Content $out; $done=$true }
}
"=== final funnel status ===" | Add-Content $out
& $ts funnel status 2>&1 | Add-Content $out
"=== final serve json ===" | Add-Content $out
& $ts serve status --json 2>&1 | Add-Content $out
"TS-MOVE2-DONE" | Add-Content $out
