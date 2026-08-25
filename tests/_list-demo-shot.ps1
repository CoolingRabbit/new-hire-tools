# Dev helper (ASCII only): launch ListDemo, verify badges, toggle check, drag row.
# Steps: shot initial -> uncheck row3 -> recheck -> drag row2 to top (mid shot) -> shot after settle.

$sig = @'
using System;
using System.Runtime.InteropServices;
public class U32D {
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
[void][U32D]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms,System.Drawing

$exe = Join-Path $PSScriptRoot 'ListDemo.exe'
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($p.HasExited) { Write-Host 'PROCESS EXITED EARLY'; exit 1 }
$hwnd = $p.MainWindowHandle

function GetRect {
    $r = New-Object U32D+RECT
    [void][U32D]::GetWindowRect($hwnd, [ref]$r)
    return $r
}
function Shot($name) {
    $r = GetRect
    $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hgt)))
    $bmp.Save((Join-Path $PSScriptRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "SHOT: $name"
}
function MoveTo($x, $y) { [void][U32D]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 60 }
function ClickAt($x, $y) {
    MoveTo $x $y
    [U32D]::mouse_event(0x0002, 0, 0, 0, 0)
    [U32D]::mouse_event(0x0004, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 300
}

$r0 = GetRect
# demo form has a real title bar: client offset ~ (8, 31)
$cx = $r0.Left + 8; $cy = $r0.Top + 31
# list at client (16,16); row i center client-y = 16 + i*34 + 17
$rowX = $cx + 100
$r0y = $cy + 33; $r1y = $cy + 67; $r2y = $cy + 101; $r3y = $cy + 135

Shot 'list-1-initial.png'

# toggle row3 off and on (badge should disappear then come back)
ClickAt $rowX $r3y
Shot 'list-2-unchecked.png'
ClickAt $rowX $r3y

# drag row2 (SG-NAS) to the top
MoveTo $rowX $r2y
[U32D]::mouse_event(0x0002, 0, 0, 0, 0)   # left down
Start-Sleep -Milliseconds 200
$steps = 8
for ($i = 1; $i -le $steps; $i++) {
    $y = $r2y - ($r2y - $r0y - 10) * $i / $steps
    MoveTo $rowX ([int]$y)
    if ($i -eq 4) { Shot 'list-3-mid-drag.png' }
}
Start-Sleep -Milliseconds 150
[U32D]::mouse_event(0x0004, 0, 0, 0, 0)   # left up
Start-Sleep -Milliseconds 600             # settle animation
Shot 'list-4-after-drop.png'

Stop-Process -Id $p.Id -Force
Write-Host 'DONE'
