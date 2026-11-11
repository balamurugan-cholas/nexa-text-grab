$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$publishDir = Join-Path $projectDir 'publish'
$installerDir = Join-Path $projectDir 'installer'
$issc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $issc)) {
    throw "Inno Setup compiler not found at $issc"
}

dotnet publish (Join-Path $projectDir 'NexaTextGrab.csproj') -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
& $issc (Join-Path $projectDir 'NexaTextGrab.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

Write-Host "Installer created: $(Join-Path $installerDir 'NexaTextGrabSetup.exe')"
