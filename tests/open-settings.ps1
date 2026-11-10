Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SettiNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "Fresh2D*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
[SettiNative]::SetProcessDPIAware() | Out-Null
[SettiNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
# Esc to stop play
[SettiNative]::keybd_event(0x1B,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80; [SettiNative]::keybd_event(0x1B,0,2,[IntPtr]::Zero)
Start-Sleep -Milliseconds 600
# click Configurações (physical ~ 300,106)
[SettiNative]::SetCursorPos(300, 106) | Out-Null
[SettiNative]::mouse_event(0x02,0,0,0,[IntPtr]::Zero); [SettiNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 1200
Write-Output "OPENED"
