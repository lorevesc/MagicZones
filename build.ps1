# Builds MagicZones.csproj in Release with Visual Studio's MSBuild -> bin\Release\MagicZones.exe
# Target: .NET Framework 4.8 (built into Windows 10/11): nothing to install to run it.
#   -Installer  also compiles installer\MagicZones.iss with Inno Setup 6 -> dist\MagicZones-Setup-x.y.z.exe
#   -Run        starts the freshly built exe
param([switch]$Installer, [switch]$Run, [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
        if (-not $found) { $found = & $vswhere -latest -prerelease -products * -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
        if ($found -and (Test-Path $found)) { return $found }
    }
    $candidates = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio" `
        -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*\Current\Bin\amd64\MSBuild.exe' } |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    throw 'MSBuild non trovato: installa Visual Studio o le Build Tools.'
}

function Find-Iscc {
    foreach ($p in "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") {
        if (Test-Path $p) { return $p }
    }
    return $null
}

if (-not (Test-Path (Join-Path $root 'assets\MagicZones.ico'))) {
    throw "assets\MagicZones.ico mancante (ripristinalo da git, o rigeneralo con: MagicZones.exe --export-icon assets\MagicZones.ico)"
}

$outDir = Join-Path $root "bin\$Configuration"
$exe = Join-Path $outDir 'MagicZones.exe'

# A running copy locks the exe. If we can't stop it (e.g. it runs as administrator), move it aside:
# a running exe can be renamed; the next restart picks up the new build.
Get-Process MagicZones -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
Start-Sleep -Milliseconds 200
Get-ChildItem $outDir -Filter 'MagicZones.old-*.exe' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
if (Test-Path $exe) {
    try { [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None').Dispose() }
    catch {
        $aside = Join-Path $outDir ("MagicZones.old-{0:yyyyMMddHHmmss}.exe" -f (Get-Date))
        Rename-Item $exe $aside
        Write-Host "MagicZones in esecuzione: vecchio exe spostato in $(Split-Path $aside -Leaf). Riavvialo per usare la nuova versione." -ForegroundColor Yellow
    }
}

$msbuild = Find-MSBuild
& $msbuild (Join-Path $root 'MagicZones.csproj') /nologo /verbosity:minimal /restore:false `
    "/p:Configuration=$Configuration" /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita ($LASTEXITCODE)" }
Write-Host "OK -> $exe" -ForegroundColor Green

if ($Installer) {
    if ($Configuration -ne 'Release') { throw "L'installer si crea solo dalla build Release" }
    $iscc = Find-Iscc
    if (-not $iscc) { throw "Inno Setup 6 non trovato (https://jrsoftware.org/isdl.php): installalo per creare l'installer." }
    & $iscc /Q (Join-Path $root 'installer\MagicZones.iss')
    if ($LASTEXITCODE -ne 0) { throw "Creazione installer fallita ($LASTEXITCODE)" }
    Get-ChildItem (Join-Path $root 'dist') -Filter 'MagicZones-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1 |
        ForEach-Object { Write-Host "Installer -> $($_.FullName)" -ForegroundColor Green }
}

if ($Run) { Start-Process $exe }
