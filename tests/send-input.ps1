param([string]$Keys = "e", [int]$WaitMs = 1200)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class InputNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
}
"@
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "Fresh2D*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
[InputNative]::SetProcessDPIAware() | Out-Null
[InputNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
[InputNative]::SetCursorPos(865, 420) | Out-Null
[InputNative]::mouse_event(0x02,0,0,0,[IntPtr]::Zero)
[InputNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 300
$fg = [InputNative]::GetForegroundWindow()
$sb = New-Object System.Text.StringBuilder 256
[InputNative]::GetWindowText($fg, $sb, 256) | Out-Null
Write-Output ("FOREGROUND: " + $sb.ToString())
foreach ($ch in $Keys.ToCharArray()) {
    $vk = [byte][char]([string]$ch).ToUpper()
    [InputNative]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [InputNative]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}
Start-Sleep -Milliseconds $WaitMs
Write-Output ("SENT " + $Keys)
