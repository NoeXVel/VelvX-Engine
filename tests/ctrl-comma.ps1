param([string]$TitleLike = "Fresh2D")
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CtrlCommaNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
[CtrlCommaNative]::SetProcessDPIAware() | Out-Null
[CtrlCommaNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
[CtrlCommaNative]::keybd_event(0x11,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[CtrlCommaNative]::keybd_event(0xBC,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[CtrlCommaNative]::keybd_event(0xBC,0,2,[IntPtr]::Zero)
[CtrlCommaNative]::keybd_event(0x11,0,2,[IntPtr]::Zero)
Start-Sleep -Milliseconds 1200
Write-Output "SENT CTRL+,"
