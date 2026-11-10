[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $children.Count; $i++) {
    $el = $children.Item($i)
    if ($el.Current.ProcessId -eq $p.Id) {
        Write-Output ("WINDOW: " + $el.Current.Name + " class=" + $el.Current.ClassName)
    }
}
