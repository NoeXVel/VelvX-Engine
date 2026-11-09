param(
    [string]$ProjectName = "Teste3D",
    [string]$TemplateMatch = "Projeto 3D"
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$process = Get-Process VelvX.Engine | Select-Object -First 1
if (-not $process) { throw "VelvX.Engine nao esta em execucao." }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$byProcess = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byProcess)
if (-not $window) { throw "Janela do VelvX nao encontrada." }

function Find-All($controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    return $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# 1. Navigate to New Project
$navButton = $null
foreach ($b in (Find-All ([System.Windows.Automation.ControlType]::Button))) {
    if ($b.Current.Name -eq "Novo projeto") { $navButton = $b; break }
}
if (-not $navButton) {
    foreach ($b in (Find-All ([System.Windows.Automation.ControlType]::Button))) {
        if ($b.Current.Name -like "*Novo projeto*") { $navButton = $b; break }
    }
}
if (-not $navButton) { throw "Botao Novo projeto nao encontrado." }
$navButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 600

# 2. Select the 3D template radio
$radio = $null
foreach ($r in (Find-All ([System.Windows.Automation.ControlType]::RadioButton))) {
    Write-Output "RADIO: $($r.Current.Name)"
    if ($r.Current.Name -like "*$TemplateMatch*") { $radio = $r }
}
if (-not $radio) { throw "Template '$TemplateMatch' nao encontrado." }
$radio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 400

# 3. Fill name and create
$edits = Find-All ([System.Windows.Automation.ControlType]::Edit)
if ($edits.Count -lt 1) { throw "Campo de nome nao encontrado." }
$edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($ProjectName)
Start-Sleep -Milliseconds 300

$create = $null
foreach ($b in (Find-All ([System.Windows.Automation.ControlType]::Button))) {
    if ($b.Current.Name -like "*Criar projeto*") { $create = $b; break }
}
if (-not $create) { throw "Botao Criar projeto nao encontrado." }
$create.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Projeto '$ProjectName' solicitado com template '$($radio.Current.Name)'."
