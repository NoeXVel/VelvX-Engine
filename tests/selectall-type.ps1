param([string]$Text)
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys("^a")
Start-Sleep -Milliseconds 100
$ws.SendKeys($Text)
Start-Sleep -Milliseconds 200
Write-Output "TYPED $Text"
