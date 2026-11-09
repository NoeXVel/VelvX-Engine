param(
    [string]$Key = "D",
    [int]$HoldMs = 900,
    [string]$Out = "C:\Users\Gean\Documents\MyEngine\tests\editor-play-move.png"
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class PlayMoveNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[PlayMoveNative]::SetProcessDPIAware() | Out-Null

$proc = Get-Process | Where-Object { $_.MainWindowTitle -like "*VelvX Engine*" } | Select-Object -First 1
if (-not $proc) { Write-Output "NO_WINDOW"; exit 1 }
$hwnd = $proc.MainWindowHandle
[PlayMoveNative]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 300

$vk = [byte][char]$Key
[PlayMoveNative]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)          # key down
Start-Sleep -Milliseconds $HoldMs
[PlayMoveNative]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)          # key up
Start-Sleep -Milliseconds 250

$rect = New-Object PlayMoveNative+RECT
[PlayMoveNative]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[PlayMoveNative]::PrintWindow($hwnd, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "CAPTURED $Out"
