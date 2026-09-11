# Builds bin\MagicZones.exe with the Roslyn compiler shipped with Visual Studio.
# Target: .NET Framework 4.8 (built into Windows 10/11) -> nothing to install to run it.
param([switch]$Run)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Find-Csc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    $candidates = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio" `
        -Recurse -Filter csc.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*\Roslyn\csc.exe' } |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    throw 'Compilatore Roslyn (csc.exe) non trovato: installa Visual Studio o le Build Tools.'
}

$csc = Find-Csc
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$bin = Join-Path $root 'bin'
$exe = Join-Path $bin 'MagicZones.exe'
$ico = Join-Path $root 'assets\MagicZones.ico'
New-Item -ItemType Directory -Force $bin, (Join-Path $root 'assets') | Out-Null

# Don't fight a running copy for the file lock.
Get-Process MagicZones -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
Start-Sleep -Milliseconds 200

function Invoke-Csc([string]$icon) {
    $refs = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms' |
        ForEach-Object { "/r:$fx\$_.dll" }
    $cscArgs = @('/nologo', '/noconfig', '/nostdlib+', '/target:winexe', '/platform:anycpu', '/optimize+',
        '/langversion:latest', '/utf8output', '/warn:4', '/nowarn:1591', "/out:$exe",
        "/win32manifest:$(Join-Path $root 'app.manifest')") + $refs
    if ($icon) { $cscArgs += "/win32icon:$icon" }
    $cscArgs += (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita ($LASTEXITCODE)" }
}

if (-not (Test-Path $ico)) {
    Invoke-Csc $null
    & $exe --export-icon $ico | Out-Null
    Start-Sleep -Milliseconds 300
}
Invoke-Csc $ico
Copy-Item (Join-Path $root 'App.config') "$exe.config" -Force

Write-Host "OK -> $exe" -ForegroundColor Green
if ($Run) { Start-Process $exe }
