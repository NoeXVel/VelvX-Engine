param([string]$NameLike = "Config*", [string]$TitleLike = "Fresh2D")
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
foreach ($w in $windows) {
    Write-Output ("WIN: " + $w.Current.Name)
    $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($el in $all) {
        $n = $el.Current.Name
        if ($n -and ($n -like $NameLike)) {
            Write-Output ("MATCH: [" + $el.Current.ControlType.ProgrammaticName + "] " + $n)
            try {
                $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $pattern.Invoke()
                Write-Output "INVOKED"
                exit 0
            } catch { Write-Output ("NO_INVOKE: " + $_.Exception.Message) }
        }
    }
}
Write-Output "NOT_FOUND"
exit 1
