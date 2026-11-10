param([string]$In, [string]$Out, [int]$X, [int]$Y, [int]$W, [int]$H)
Add-Type -AssemblyName System.Drawing
$b = [System.Drawing.Bitmap]::FromFile($In)
$r = New-Object System.Drawing.Rectangle($X, $Y, $W, $H)
$c = $b.Clone($r, $b.PixelFormat)
$c.Save($Out)
$c.Dispose(); $b.Dispose()
Write-Output "CROPPED"
