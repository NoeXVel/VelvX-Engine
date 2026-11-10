param([string]$Key = "d", [int]$HoldMs = 900)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class HoldNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "Fresh2D*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
[HoldNative]::SetProcessDPIAware() | Out-Null
[HoldNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
[HoldNative]::SetCursorPos(865, 420) | Out-Null
[HoldNative]::mouse_event(0x02,0,0,0,[IntPtr]::Zero)
[HoldNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 250
$vk = [byte][char]$Key.ToUpper()
[HoldNative]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[HoldNative]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 250
Write-Output "HELD $Key ${HoldMs}ms"
