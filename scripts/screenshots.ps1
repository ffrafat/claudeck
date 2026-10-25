# Takes the README and website screenshots from ClauDeck's demo mode: sample accounts
# only, so nobody's real accounts or email addresses end up in a picture.
#   .\scripts\screenshots.ps1 [-Exe <path to ClauDeck.exe>]
param([string]$Exe = "$env:LOCALAPPDATA\Programs\ClauDeck\ClauDeck.exe")
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\docs\assets\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Shot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

$shots = @(
    @{ Name = 'overview-light'; Page = 'overview'; Theme = 'light' },
    @{ Name = 'overview-dark';  Page = 'overview'; Theme = 'dark' },   # the same view, for dark-mode visitors
    @{ Name = 'account-light';  Page = 'work';     Theme = 'light' },
    @{ Name = 'about-light';    Page = 'about';    Theme = 'light' }
)
foreach ($s in $shots) {
    $p = Start-Process $Exe -ArgumentList '--demo', '--page', $s.Page, '--theme', $s.Theme -PassThru
    for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
    [Shot]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 3   # let Mica and the cards settle
    $r = New-Object Shot+RECT; [Shot]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [Shot]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $out "$($s.Name).png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Stop-Process -Id $p.Id -Force
    "saved $($s.Name).png"
}
