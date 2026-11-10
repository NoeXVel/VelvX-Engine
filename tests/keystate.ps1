Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KeyStateNative { [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk); }
"@
$names = @(
 @(0x11,'CTRL'), @(0x10,'SHIFT'), @(0x12,'ALT'), @(0x0D,'ENTER'), @(0x1B,'ESC'),
 @(0x25,'LEFT'), @(0x26,'UP'), @(0x27,'RIGHT'), @(0x28,'DOWN'), @(0x09,'TAB'), @(0x20,'SPACE'),
 @(0x5A,'Z'), @(0x51,'Q'), @(0x45,'E'), @(0x44,'D'), @(0x41,'A'), @(0x57,'W'), @(0x53,'S'), @(0xBC,'COMMA')
)
foreach ($pair in $names) {
    $state = [KeyStateNative]::GetAsyncKeyState($pair[0])
    if ($state -ne 0) { Write-Output ("DOWN: " + $pair[1] + " state=" + $state) }
}
Write-Output "CHECKED"
