param([string]$TitleLike = "DemoForja", [int]$Samples = 6)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function Get-Value($id) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $el = $all.Item($i)
        if ($el.Current.AutomationId -eq $id) {
            try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return "" }
        }
    }
    return ""
}
for ($s = 0; $s -lt $Samples; $s++) {
    $name = Get-Value "NameField"
    $x = Get-Value "PosXField"
    $y = Get-Value "PosYField"
    $z = Get-Value "PosZField"
    $sx = Get-Value "SizeXField"
    Write-Output ("SAMPLE $s name='$name' pos=$x,$y,$z size=$sx")
    Start-Sleep -Milliseconds 700
}
