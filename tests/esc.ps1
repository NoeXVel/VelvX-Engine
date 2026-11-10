Add-Type @"
using System;
using System.Runtime.InteropServices;
public class EscNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
$p = Get-Process VelvX.Engine | Where-Object { $_.MainWindowTitle -like "Fresh3D*" } | Select-Object -First 1
[EscNative]::SetProcessDPIAware() | Out-Null
[EscNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
[EscNative]::keybd_event(0x1B,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80; [EscNative]::keybd_event(0x1B,0,2,[IntPtr]::Zero)
Start-Sleep -Milliseconds 800
Write-Output "ESC"
