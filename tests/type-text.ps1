param([string]$Text = "atacar")
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys($Text)
Start-Sleep -Milliseconds 250
Write-Output "TYPED $Text"
