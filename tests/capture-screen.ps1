param(
    [string]$Out = "C:\Users\Gean\Documents\MyEngine\tests\screen-capture.png",
    [string]$TitleLike = "VelvX Engine",
    [string]$Key = "",
    [int]$WaitMs = 1800,
    [string]$HoldKey = "",
    [int]$HoldMs = 900
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ScreenCapNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[ScreenCapNative]::SetProcessDPIAware() | Out-Null

$proc = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $proc) {
    $proc = Get-Process | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
}
if (-not $proc) { Write-Output "NO_WINDOW"; exit 1 }
$hwnd = $proc.MainWindowHandle
[ScreenCapNative]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 600

if ($Key -ne "") {
    $ws = New-Object -ComObject WScript.Shell
    $ws.SendKeys($Key)
    Start-Sleep -Milliseconds $WaitMs
}

if ($HoldKey -ne "") {
    $vk = [byte][char]$HoldKey
    [ScreenCapNative]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $HoldMs
    [ScreenCapNative]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

$rect = New-Object ScreenCapNative+RECT
[ScreenCapNative]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "CAPTURED ${w}x${h} -> $Out"
