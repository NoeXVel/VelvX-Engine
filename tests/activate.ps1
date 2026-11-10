param([string]$TitleLike = "Fresh2D")
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ActNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[ActNative]::SetProcessDPIAware() | Out-Null
$p = Get-Process VelvX.Engine -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $p) { Write-Output "NO_PROC"; exit 1 }
$hwnd = $p.MainWindowHandle
$fgThread = [ActNative]::GetWindowThreadProcessId([ActNative]::GetForegroundWindow(), [ref]([uint32]$fgPid))
$myThread = [ActNative]::GetCurrentThreadId()
[ActNative]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
[ActNative]::ShowWindow($hwnd, 9) | Out-Null
[ActNative]::BringWindowToTop($hwnd) | Out-Null
$ok = [ActNative]::SetForegroundWindow($hwnd)
[ActNative]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
Start-Sleep -Milliseconds 500
$fg = [ActNative]::GetForegroundWindow()
$sb = New-Object System.Text.StringBuilder 256
[ActNative]::GetWindowText($fg, $sb, 256) | Out-Null
$rect = New-Object ActNative+RECT
[ActNative]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
Write-Output ("SET_OK=" + $ok + " FOREGROUND=" + $sb.ToString() + " RECT=" + $rect.Left + "," + $rect.Top + "," + $rect.Right + "," + $rect.Bottom)
