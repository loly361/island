<#
  Builds installer\output\Island-Setup.exe and Island-Setup.exe.sha256.

  .\installer\build-installer.ps1                      # publish + compile, version from Island.csproj
  .\installer\build-installer.ps1 -Version 1.2.0
  .\installer\build-installer.ps1 -SkipPublish -PayloadDir C:\path\to\ready\build
#>
param(
    [string]$Version,
    [switch]$SkipPublish,
    [string]$PayloadDir
)

$ErrorActionPreference = 'Stop'
$InstallerDir = $PSScriptRoot
$RepoRoot = Split-Path $InstallerDir -Parent
$Csproj = Join-Path $RepoRoot 'src\Island\Island.csproj'
$Iss = Join-Path $InstallerDir 'Island.iss'
$OutputDir = Join-Path $InstallerDir 'output'
$SetupExe = Join-Path $OutputDir 'Island-Setup.exe'

if (-not $Version) {
    [xml]$proj = Get-Content -Raw -Encoding UTF8 $Csproj
    $Version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    if (-not $Version) { throw "Version not found in $Csproj; pass -Version." }
}
$Version = "$Version".Trim().TrimStart('v')

if (-not $PayloadDir) { $PayloadDir = Join-Path $InstallerDir 'payload' }
# Relative paths are taken from the PowerShell location (not the process folder, which can differ).
$PayloadDir = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $PayloadDir))

Write-Host "Island installer: version $Version, payload $PayloadDir"

# Publishing wipes the payload folder first, so it must be a folder of our own: strictly inside installer\
# (GetFullPath has already resolved "..") and reached without symlinks/junctions, which could lead elsewhere.
function Assert-SafePayloadDir([string]$Dir) {
    $root = [IO.Path]::GetFullPath($InstallerDir).TrimEnd('\')
    $dir = $Dir.TrimEnd('\')
    if (-not $dir.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "PayloadDir '$Dir' is outside '$root'. It is deleted before publishing, so only a folder inside installer\ is allowed. Use -SkipPublish for a ready build elsewhere."
    }
    $p = $dir
    while ($p.Length -gt $root.Length) {
        $item = Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "PayloadDir '$Dir' goes through a symbolic link or junction ('$p'); refusing to delete it."
        }
        $p = Split-Path $p -Parent
    }
    if (Test-Path -LiteralPath $dir) {
        $link = Get-ChildItem -LiteralPath $dir -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($link) { throw "PayloadDir '$Dir' contains a link ('$($link.FullName)'); refusing to delete it." }
    }
}

if (-not $SkipPublish) {
    Assert-SafePayloadDir $PayloadDir
    if (Test-Path -LiteralPath $PayloadDir) { Remove-Item -LiteralPath $PayloadDir -Recurse -Force }
    & dotnet publish $Csproj -c Release -o $PayloadDir `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:DebugType=None -p:DebugSymbols=false `
        "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$required = @(
    'Island.exe', 'Island.dll', 'Island.deps.json', 'Island.runtimeconfig.json',
    'NAudio.Core.dll', 'NAudio.Wasapi.dll', 'System.Data.OleDb.dll',
    'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll',
    'runtimes\win\lib\net9.0\System.Data.OleDb.dll'
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $PayloadDir $_)) }
if ($missing) { throw "Payload is incomplete, missing: $($missing -join ', ')" }

function Find-Iscc {
    if ($env:ISCC -and (Test-Path $env:ISCC)) { return $env:ISCC }
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw 'ISCC.exe not found. Install Inno Setup 6 (winget install -e --id JRSoftware.InnoSetup) or set $env:ISCC.'
}
$Iscc = Find-Iscc
Write-Host "ISCC: $Iscc"

& $Iscc "/DAppVersion=$Version" "/DPayloadDir=$PayloadDir" "/O$OutputDir" $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
if (-not (Test-Path $SetupExe)) { throw "$SetupExe was not created" }

$hash = (Get-FileHash -Algorithm SHA256 $SetupExe).Hash.ToLowerInvariant()
$line = "$hash  Island-Setup.exe`n"
[IO.File]::WriteAllText("$SetupExe.sha256", $line, (New-Object System.Text.UTF8Encoding $false))

$size = (Get-Item $SetupExe).Length
Write-Host ''
Write-Host "Setup:  $SetupExe"
Write-Host ("Size:   {0:N0} bytes ({1:N1} MB)" -f $size, ($size / 1MB))
Write-Host "SHA256: $hash"
