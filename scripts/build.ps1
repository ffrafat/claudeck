# Builds ClauDeck and installs it for this Windows user, for working on it.
# To make the installer and portable zip people download, use release.ps1.
#   .\scripts\build.ps1             build and install to %LOCALAPPDATA%\Programs\ClauDeck
#   .\scripts\build.ps1 -NoInstall  build only
param([switch]$NoInstall)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ClauDeck' | Resolve-Path
Set-Location $project

$programs = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'
$installDir = Join-Path $programs 'ClauDeck'
$exeName = 'ClauDeck.exe'
# Where the app lived under the names it had before.
$oldInstalls = @{ 'ClaudeAccounts' = 'ClaudeAccounts.exe'; 'ClawDeck' = 'ClawDeck.exe' }

# The exe's own icon is drawn by the app itself, so the first build has none; it draws
# one, and the publish below embeds it. Delete app.ico to have it drawn again.
if (-not (Test-Path 'app.ico')) {
    dotnet build -c Release --nologo -v quiet
    if ($LASTEXITCODE) { throw 'build failed' }
    $built = Get-ChildItem "$env:TEMP\claudeck-build\bin\Release" -Recurse -Filter $exeName | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    & $built.FullName --write-icon (Join-Path $project 'app.ico') | Out-Null
    while (-not (Test-Path 'app.ico')) { Start-Sleep -Milliseconds 200 }
}

if ($NoInstall) { dotnet build -c Release --nologo -v quiet; exit $LASTEXITCODE }

# A running copy holds its exe open, under any of its names.
Get-Process -Name 'ClauDeck', 'ClawDeck', 'ClaudeAccounts' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
# The folder holds nothing but earlier builds of this app; a clean copy keeps files
# from an older version from being loaded beside the new one.
if (Test-Path (Join-Path $installDir $exeName)) { Get-ChildItem $installDir | Remove-Item -Recurse -Force }
dotnet publish -c Release --nologo -v quiet -o $installDir
if ($LASTEXITCODE) { throw 'publish failed' }
# --install also moves shortcuts made under the old names over to this exe.
$p = Start-Process -FilePath (Join-Path $installDir $exeName) -ArgumentList '--install' -PassThru -Wait
if ($p.ExitCode) { throw "install step failed ($($p.ExitCode))" }
foreach ($old in $oldInstalls.GetEnumerator()) {
    $dir = Join-Path $programs $old.Key
    if (Test-Path (Join-Path $dir $old.Value)) { Remove-Item $dir -Recurse -Force }
}
"Installed to $installDir"
