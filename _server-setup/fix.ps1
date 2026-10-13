$out = 'C:\Saudization\Saudization\_server-setup\fix.log'
Import-Module WebAdministration
"before: HelpDeskProxy state = $((Get-Website 'HelpDeskProxy').State)" | Out-File $out
try {
    Start-WebAppPool 'DefaultAppPool' -ErrorAction SilentlyContinue
    Start-Website 'HelpDeskProxy'
    Start-Sleep 3
    "after:  HelpDeskProxy state = $((Get-Website 'HelpDeskProxy').State)" | Add-Content $out
    "DefaultAppPool state = $((Get-WebAppPoolState 'DefaultAppPool').Value)" | Add-Content $out
} catch { "START FAILED: $($_.Exception.Message)" | Add-Content $out }
"FIX-DONE" | Add-Content $out
