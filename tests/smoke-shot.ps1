# Dev helper (ASCII only): launch the toolbox exe, click each tab, screenshot, kill.
# DPI strategy: mark THIS powershell process DPI-aware as early as possible,
# so window coordinates and screen pixels are both physical (no scaling math).
# Beta layout: borderless 640x780, title bar 40, segmented tab bar at y=52 h=42.
# Tab segment centers (client coords): x = 144 / 280 / 416, y = 73.

$sig = @'
using System;
using System.Runtime.InteropServices;
public class U32 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, int extra);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
Add-Type -TypeDefinition $sig -ReferencedAssemblies System.Drawing
[void][U32]::SetProcessDPIAware()

Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\NewHireToolbox.exe'
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) { Write-Host 'PROCESS EXITED EARLY'; exit 1 }
$hwnd = $p.MainWindowHandle

function GetRect {
    $r = New-Object U32+RECT
    [void][U32]::GetWindowRect($hwnd, [ref]$r)
    return $r
}
function Shot($name) {
    $r = GetRect
    $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hgt)))
    $out = Join-Path $PSScriptRoot $name
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "SHOT: $name ($w x $hgt)"
}
function ClickClient($relX, $relY) {
    $r = GetRect
    [void][U32]::SetCursorPos($r.Left + $relX, $r.Top + $relY)
    Start-Sleep -Milliseconds 250
    [U32]::mouse_event(0x0002, 0, 0, 0, 0)
    [U32]::mouse_event(0x0004, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 700
}

[void][U32]::SetWindowPos($hwnd, [IntPtr](-1), 60, 60, 0, 0, 0x0001)  # topmost + move to primary monitor
[void][U32]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 600

ClickClient 184 73
Shot 'shot-tab1.png'
ClickClient 320 73
Shot 'shot-tab2.png'
ClickClient 456 73
Shot 'shot-tab3.png'

Stop-Process -Id $p.Id -Force
Write-Host 'PROCESS KILLED OK'
