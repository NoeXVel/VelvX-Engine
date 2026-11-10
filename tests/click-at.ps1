param([int]$X, [int]$Y)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ClickNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
"@
[ClickNative]::SetProcessDPIAware() | Out-Null
[ClickNative]::SetCursorPos($X, $Y) | Out-Null
[ClickNative]::mouse_event(0x02,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[ClickNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
Write-Output "CLICK $X,$Y"
