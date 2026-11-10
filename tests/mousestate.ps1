Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MouseStateNative {
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetCapture();
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int m);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
$p = New-Object MouseStateNative+POINT
[MouseStateNative]::GetCursorPos([ref]$p) | Out-Null
Write-Output ("CURSOR " + $p.X + "," + $p.Y)
foreach ($vk in 0x01,0x02,0x04) {
    $s = [MouseStateNative]::GetAsyncKeyState($vk)
    if ($s -ne 0) { Write-Output ("MOUSE_BTN_DOWN vk=" + $vk + " state=" + $s) }
}
$sb = New-Object System.Text.StringBuilder 256
[MouseStateNative]::GetWindowText([MouseStateNative]::GetForegroundWindow(), $sb, 256) | Out-Null
Write-Output ("FOREGROUND=" + $sb.ToString())
Write-Output ("CAPTURE=" + [MouseStateNative]::GetCapture())
