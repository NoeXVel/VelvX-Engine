param([string]$Out = "C:\Users\Gean\Documents\MyEngine\tests\capture.png", [string]$TitleLike = "VelvX Engine", [int]$WaitMs = 800)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CapAppNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[CapAppNative]::SetProcessDPIAware() | Out-Null
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_APP_WINDOW"; exit 1 }
$hwnd = $p.MainWindowHandle
[CapAppNative]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds $WaitMs
$rect = New-Object CapAppNative+RECT
[CapAppNative]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[CapAppNative]::PrintWindow($hwnd, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "CAPTURED ${w}x${h} -> $Out"
Write-Output "TITLE: $($p.MainWindowTitle)"
