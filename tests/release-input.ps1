Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ReleaseInputNative {
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
[ReleaseInputNative]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
[ReleaseInputNative]::mouse_event(0x10,0,0,0,[IntPtr]::Zero)
[ReleaseInputNative]::mouse_event(0x40,0,0,0,[IntPtr]::Zero)
foreach ($vk in 0x10,0x11,0x12,0x5B) {
    [ReleaseInputNative]::keybd_event([byte]$vk, 0, 2, [IntPtr]::Zero)
}
Write-Output "RELEASED"
