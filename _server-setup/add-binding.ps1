$out='C:\Saudization\Saudization\_server-setup\bind.log'
Import-Module WebAdministration
$site='Saudization'
"--- bindings BEFORE (all sites) ---" | Out-File $out
Get-WebBinding | ForEach-Object { $_.ItemXPath -replace ".*@name='([^']+)'.*",'$1' | Out-Null }
foreach($s in (Get-Website)){ foreach($b in $s.bindings.Collection){ "$($s.Name): $($b.protocol) $($b.bindingInformation) [$($s.State)]" | Add-Content $out } }
# add host-header bindings on :80 ONLY for the Saudization site (host-routed; no port grab, no 443)
foreach($h in @('saudization','saudization.sharbatlyfruit.com')){
  $have = Get-WebBinding -Name $site -Protocol http -Port 80 -HostHeader $h -ErrorAction SilentlyContinue
  if(-not $have){ New-WebBinding -Name $site -Protocol http -IPAddress '*' -Port 80 -HostHeader $h; "ADDED http *:80:$h -> $site" | Add-Content $out }
  else { "exists: $h" | Add-Content $out }
}
Start-Sleep 1
"--- bindings AFTER (all sites) ---" | Add-Content $out
foreach($s in (Get-Website)){ foreach($b in $s.bindings.Collection){ "$($s.Name): $($b.protocol) $($b.bindingInformation) [$($s.State)]" | Add-Content $out } }
"--- site states ---" | Add-Content $out
Get-Website | ForEach-Object { "$($_.Name) = $($_.State)" } | Add-Content $out
"BIND-DONE" | Add-Content $out
