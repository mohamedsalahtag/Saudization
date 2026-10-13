$out='C:\Saudization\Saudization\_server-setup\fix2.log'
Import-Module WebAdministration
$ts = Get-Service | Where-Object { $_.Name -match 'tailscale' -or $_.DisplayName -match 'tailscale' } | Select-Object -First 1
"tailscale service: $($ts.Name)  status=$($ts.Status)" | Out-File $out
"HelpDeskProxy before: $((Get-Website 'HelpDeskProxy').State)" | Add-Content $out

# 1) release 443 by stopping Tailscale briefly
if ($ts) { Stop-Service -Name $ts.Name -Force -ErrorAction SilentlyContinue; Start-Sleep 3; "tailscale stopped: $((Get-Service $ts.Name).Status)" | Add-Content $out }

# 2) start the proxy site so http.sys claims 443
try { Start-Website 'HelpDeskProxy' -ErrorAction Stop; "Start-Website ok" | Add-Content $out } catch { "Start-Website EX: $($_.Exception.Message)" | Add-Content $out }
Start-Sleep 2
"HelpDeskProxy after start: $((Get-Website 'HelpDeskProxy').State)" | Add-Content $out

# 3) bring Tailscale back (IIS already owns 443, so it coexists on its tailnet IPs)
if ($ts) { Start-Service -Name $ts.Name -ErrorAction SilentlyContinue; Start-Sleep 4; "tailscale restarted: $((Get-Service $ts.Name).Status)" | Add-Content $out }

Start-Sleep 2
"HelpDeskProxy FINAL: $((Get-Website 'HelpDeskProxy').State)" | Add-Content $out
# local proxy check (loopback with host header -> should 200/redirect via Kestrel :5000)
try { $r = Invoke-WebRequest 'http://127.0.0.1:5000/' -UseBasicParsing -TimeoutSec 10; "backend :5000 = $($r.StatusCode)" | Add-Content $out } catch { "backend :5000 err: $($_.Exception.Message)" | Add-Content $out }
"FIX2-DONE" | Add-Content $out
