Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KeyNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
"@
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
[KeyNative]::SetProcessDPIAware() | Out-Null
[KeyNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
# click viewport center (physical 1938x1038; viewport around x=865,y=400)
[KeyNative]::SetCursorPos(865, 420) | Out-Null
[KeyNative]::mouse_event(0x02,0,0,0,[IntPtr]::Zero)
[KeyNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 250
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys("e")
Start-Sleep -Milliseconds 1500
$ws.SendKeys("d")
Start-Sleep -Milliseconds 700
Write-Output "SENT"
