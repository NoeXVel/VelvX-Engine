param([string]$TitleLike = "DemoForja")
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("COUNT=" + $all.Count)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all.Item($i)
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
        $value = ""
        try { $value = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}
        Write-Output ("EDIT name='" + $el.Current.Name + "' id='" + $el.Current.AutomationId + "' value='" + $value + "'")
    }
}
