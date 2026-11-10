param([string]$TitleLike = "VelvX Engine")
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
Write-Output ("ROOT: " + $root.Current.Name + " class=" + $root.Current.ClassName)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("COUNT=" + $all.Count)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all.Item($i)
    $ct = $el.Current.ControlType.ProgrammaticName
    if ($el.Current.Name) { Write-Output ("$i [$ct] " + $el.Current.Name) }
}
