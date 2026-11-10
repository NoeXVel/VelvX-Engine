param([string]$TitleLike = "VelvX Engine", [int]$Occurrence = 0)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$items = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all.Item($i)
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $items += $el }
}
Write-Output ("ITEMS=" + $items.Count)
if ($items.Count -le $Occurrence) { Write-Output "NOT_FOUND"; exit 1 }
$target = $items[$Occurrence]
$pattern = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$pattern.Select()
Write-Output "SELECTED"
