# Dev diagnostic (ASCII only): test share enumeration via NetShareEnum (netapi32)
# against both NAS IPs, plus service status, to locate the net view 1702 failure.
$sig = @'
using System;
using System.Runtime.InteropServices;
public class NetApi {
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareEnum(string server, int level, out IntPtr buf, int prefmax, out int entries, out int total, ref int resume);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buf);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buf, int dummy);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHARE_INFO_0 { public string Name; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "NetShareEnum")]
    public static extern int NetShareEnum1(string server, int level, out IntPtr buf, int prefmax, out int entries, out int total, ref int resume);
}
'@
Add-Type -TypeDefinition $sig

foreach ($ip in @('192.168.100.10', '192.168.200.22')) {
    $buf = [IntPtr]::Zero
    $entries = 0; $total = 0; $resume = 0
    $rc = [NetApi]::NetShareEnum($ip, 0, [ref]$buf, -1, [ref]$entries, [ref]$total, [ref]$resume)
    Write-Host ("NetShareEnum " + $ip + " -> rc=" + $rc + " entries=" + $entries)
    if ($rc -eq 0 -and $entries -gt 0) {
        $size = [Runtime.InteropServices.Marshal]::SizeOf([type][NetApi+SHARE_INFO_0])
        for ($i = 0; $i -lt $entries; $i++) {
            $p = [IntPtr]($buf.ToInt64() + $i * $size)
            $info = [Runtime.InteropServices.Marshal]::PtrToStructure($p, [type][NetApi+SHARE_INFO_0])
            Write-Host ("   share: " + $info.Name)
        }
    }
    if ($buf -ne [IntPtr]::Zero) { [void][NetApi]::NetApiBufferFree($buf) }
}

Get-Service LanmanServer, LanmanWorkstation | Format-Table Name, Status -AutoSize
