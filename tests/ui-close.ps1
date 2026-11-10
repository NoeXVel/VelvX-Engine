param([long]$Handle)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Handle)
$pattern = $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
$pattern.Close()
Write-Output "CLOSED: $($root.Current.Name)"
