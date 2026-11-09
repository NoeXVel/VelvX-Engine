param(
    [string]$ProjectName = "Teste2D"
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$process = Get-Process VelvX.Engine | Select-Object -First 1
if (-not $process) { throw "VelvX.Engine nao esta em execucao." }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$byProcess = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byProcess)
if (-not $window) { throw "Janela do VelvX nao encontrada." }

$editType = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edits = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editType)
if ($edits.Count -lt 1) { throw "Campo de nome nao encontrado." }

$value = $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$value.SetValue($ProjectName)
Start-Sleep -Milliseconds 300

$buttonType = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonType)

$target = $null
foreach ($button in $buttons) {
    $name = $button.Current.Name
    if ($name -like "*Criar projeto*") { $target = $button; break }
}
if (-not $target) { throw "Botao Criar projeto nao encontrado." }

$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Projeto '$ProjectName' solicitado."
