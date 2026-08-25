# Dev test (ASCII only): verify probe does NOT touch the credential manager
# when the machine already has access to the server.
# Typing uses SendInput KEYEVENTF_UNICODE so the IME (e.g. Microsoft Pinyin)
# never interferes.

$sig = @'
using System;
using System.Runtime.InteropServices;
public class U32T {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, int extra);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint flags; public uint time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint flags; public uint time; public IntPtr extra; }

    public static void TypeUnicode(string text) {
        foreach (char c in text) {
            INPUT[] seq = new INPUT[2];
            seq[0].type = 1; seq[0].u.ki.wScan = (ushort)c; seq[0].u.ki.flags = 0x0004; // UNICODE down
            seq[1].type = 1; seq[1].u.ki.wScan = (ushort)c; seq[1].u.ki.flags = 0x0004 | 0x0002; // up
            SendInput(2, seq, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
'@
Add-Type -TypeDefinition $sig -ReferencedAssemblies System.Drawing
[void][U32T]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms

$root = Split-Path $PSScriptRoot -Parent
$before = (cmdkey /list) -join "`n"

$exe = Join-Path $root 'dist\NewHireToolbox.exe'
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) { Write-Host 'PROCESS EXITED EARLY'; exit 1 }
$hwnd = $p.MainWindowHandle
[void][U32T]::SetWindowPos($hwnd, [IntPtr](-1), 60, 60, 0, 0, 0x0001)
[void][U32T]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 500

function ClickAt($relX, $relY) {
    $r = New-Object U32T+RECT
    [void][U32T]::GetWindowRect($hwnd, [ref]$r)
    [void][U32T]::SetCursorPos($r.Left + $relX, $r.Top + $relY)
    Start-Sleep -Milliseconds 300
    [U32T]::mouse_event(0x0002, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 120
    [U32T]::mouse_event(0x0004, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 400
}

ClickAt 456 73        # tab3
Start-Sleep -Milliseconds 500
ClickAt 222 201       # server field
[U32T]::TypeUnicode('192.168.100.10')
ClickAt 222 257       # user field
[U32T]::TypeUnicode('test')
ClickAt 222 313       # pass field
[U32T]::TypeUnicode('test')
ClickAt 187 353       # probe button
Start-Sleep -Seconds 6

$r2 = New-Object U32T+RECT
[void][U32T]::GetWindowRect($hwnd, [ref]$r2)
$w = $r2.Right - $r2.Left; $hgt = $r2.Bottom - $r2.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r2.Left, $r2.Top, 0, 0, (New-Object System.Drawing.Size($w, $hgt)))
$bmp.Save((Join-Path $PSScriptRoot 'probe-ui-test.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Stop-Process -Id $p.Id -Force

$after = (cmdkey /list) -join "`n"
if ($before -ceq $after) { Write-Host 'CMDKEY UNCHANGED: PASS' } else { Write-Host 'CMDKEY CHANGED: FAIL' }
