# Build ArcAI.AddIn and package a flat .esriaddin (ZIP) for the ArcGIS Add-In Manager.
# Run from PowerShell:  .\build.ps1
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$root  = $PSScriptRoot
$proj  = Join-Path $root 'ArcAI.AddIn'
$out   = Join-Path $proj 'bin\Release'
$dist  = Join-Path $root 'dist'
$stage = Join-Path $dist '_stage'

Write-Host '== dotnet build =='
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

Write-Host '== staging =='
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'Install') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $stage 'Images')  | Out-Null

Copy-Item (Join-Path $proj 'Config.xml')                    (Join-Path $stage 'config.xml')
Copy-Item (Join-Path $proj 'Images\*.png')                  (Join-Path $stage 'Images')
Copy-Item (Join-Path $out  'ArcAI.AddIn.dll')               (Join-Path $stage 'Install')
Copy-Item (Join-Path $out  'Newtonsoft.Json.dll')           (Join-Path $stage 'Install')

$addin = Join-Path $dist 'ArcAI-Assistant.esriaddin'
$zip   = "$addin.zip"
if (Test-Path $addin) { Remove-Item $addin -Force }
if (Test-Path $zip)   { Remove-Item $zip -Force }
$python = Get-Command python -ErrorAction SilentlyContinue
if ($python) {
    & python (Join-Path $root 'tools\package_addin.py') $stage $zip
    if ($LASTEXITCODE -ne 0) { throw 'packaging failed' }
} else {
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
}
Rename-Item $zip $addin
Remove-Item $stage -Recurse -Force

Write-Host '== packaging InProc add-in =='
if (-not $python) { throw 'Python 3 is required to package ArcAI-InProc.esriaddin.' }
& $python.Source (Join-Path $root 'tools\package_inproc.py')
if ($LASTEXITCODE -ne 0) { throw 'InProc packaging failed' }

Write-Host ''
Write-Host ('OK -> ' + $addin)
Write-Host ('OK -> ' + (Join-Path $dist 'ArcAI-InProc.esriaddin'))
Write-Host 'Install with .\install.ps1 while ArcMap is CLOSED, then restart ArcMap.'
