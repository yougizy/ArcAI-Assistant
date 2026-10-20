# Remove the ArcAI Assistant add-in. ArcMap must be closed.
$ErrorActionPreference = 'Stop'

$guid = '{6f2c9d41-8b7a-4c3e-9f10-2d5a7e4b6c81}'
$dest = Join-Path $env:USERPROFILE ('Documents\ArcGIS\AddIns\Desktop10.8\' + $guid)
$cache = Join-Path $env:LOCALAPPDATA ('ESRI\Desktop10.8\AssemblyCache\' + $guid.ToUpper())
$inprocGuid = '{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}'
$inprocDest = Join-Path $env:USERPROFILE ('Documents\ArcGIS\AddIns\Desktop10.8\' + $inprocGuid)

if (Get-Process ArcMap -ErrorAction SilentlyContinue) {
    throw 'ArcMap is running. Close it and run .\uninstall.ps1 again.'
}
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $inprocDest) { Remove-Item $inprocDest -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'ArcAI Assistant add-ins removed.'
Write-Host 'Settings/logs under %APPDATA%\ArcAI were kept; delete that folder if you also want to remove the API key and history.'
