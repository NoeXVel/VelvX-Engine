param([string]$Out = "C:\Users\Gean\Documents\MyEngine\tests\desktop.png")
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DeskNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
"@
[DeskNative]::SetProcessDPIAware() | Out-Null
$w = [DeskNative]::GetSystemMetrics(78)
$h = [DeskNative]::GetSystemMetrics(79)
$x = [DeskNative]::GetSystemMetrics(76)
$y = [DeskNative]::GetSystemMetrics(77)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "CAPTURED ${w}x${h} at ${x},${y} -> $Out"
