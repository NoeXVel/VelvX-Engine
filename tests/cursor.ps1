Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CurNative { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); }
"@
[CurNative]::SetProcessDPIAware() | Out-Null
[CurNative]::SetCursorPos(5, 5) | Out-Null
Write-Output "CURSOR 5,5"
