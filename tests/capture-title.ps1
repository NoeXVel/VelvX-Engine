param(
    [string]$Out = "C:\Users\Gean\Documents\MyEngine\tests\capture-title.png",
    [string]$TitleLike = "*VelvX*",
    [int]$WaitMs = 400
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class CaptureTitleNative {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[CaptureTitleNative]::SetProcessDPIAware() | Out-Null
$target = [IntPtr]::Zero
$foundTitle = ""
[CaptureTitleNative]::EnumWindows({
    param($h, $l)
    if ([CaptureTitleNative]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 512
        [CaptureTitleNative]::GetWindowText($h, $sb, 512) | Out-Null
        if ($sb.ToString() -like $TitleLike) {
            $script:target = $h
            $script:foundTitle = $sb.ToString()
            return $false
        }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($target -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; exit 1 }

[CaptureTitleNative]::SetForegroundWindow($target) | Out-Null
Start-Sleep -Milliseconds $WaitMs
$rect = New-Object CaptureTitleNative+RECT
[CaptureTitleNative]::GetWindowRect($target, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[CaptureTitleNative]::PrintWindow($target, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "CAPTURED ${w}x${h} -> $Out"
Write-Output "TITLE: $foundTitle"
