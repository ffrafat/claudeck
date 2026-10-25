# Makes what people download, in artifacts\:
#   ClauDeck-Setup-<version>-x64.exe     the installer (and uninstaller)
#   ClauDeck-<version>-x64-portable.zip  unzip and run
#   SHA256SUMS.txt
# Needs the .NET 10 SDK and Inno Setup 6. The version comes from src\ClauDeck\ClauDeck.csproj.
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..' | Resolve-Path
$project = Join-Path $root 'src\ClauDeck'
$artifacts = Join-Path $root 'artifacts'
$version = ([xml](Get-Content (Join-Path $project 'ClauDeck.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in ClauDeck.csproj' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed: winget install JRSoftware.InnoSetup' }

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory $artifacts | Out-Null
# Built in a short folder: Inno Setup gives up on any file whose full path passes
# Windows' 260-character limit, and a checkout can easily sit that deep.
$stage = Join-Path $env:TEMP 'claudeck-release'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$publish = Join-Path $stage 'publish'
$branding = Join-Path $stage 'branding'

"Publishing ClauDeck $version"
dotnet publish $project -c Release --nologo -v quiet -o $publish
if ($LASTEXITCODE) { throw 'publish failed' }

"Drawing the installer artwork"
$p = Start-Process -FilePath (Join-Path $publish 'ClauDeck.exe') -ArgumentList '--write-branding', "`"$branding`"" -PassThru -Wait
if ($p.ExitCode) { throw 'drawing the installer artwork failed' }

"Packing the portable zip"
$zip = Join-Path $artifacts "ClauDeck-$version-x64-portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

"Building the installer"
# The script and the icon it names are staged too, for the same path-length reason.
Copy-Item (Join-Path $root 'installer\ClauDeck.iss') $stage
New-Item -ItemType Directory (Join-Path $stage 'src\ClauDeck') -Force | Out-Null
Copy-Item (Join-Path $project 'app.ico') (Join-Path $stage 'src\ClauDeck\app.ico')
New-Item -ItemType Directory (Join-Path $stage 'installer') -Force | Out-Null
Move-Item (Join-Path $stage 'ClauDeck.iss') (Join-Path $stage 'installer\ClauDeck.iss')
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DBrandingDir=$branding" "/O$artifacts" (Join-Path $stage 'installer\ClauDeck.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }

$setup = Join-Path $artifacts "ClauDeck-Setup-$version-x64.exe"
Get-ChildItem $setup, $zip | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } |
    Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $setup, $zip | ForEach-Object { "{0,-40} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB) }
