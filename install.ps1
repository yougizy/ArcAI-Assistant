# Install the ArcAI Assistant .esriaddin into the current user's ArcGIS AddIns folder.
# ArcMap must be closed: the DLL is locked while ArcMap is running.
# ArcGIS needs BOTH the .esriaddin file and its extracted contents in the GUID folder.
$ErrorActionPreference = 'Stop'

$root  = $PSScriptRoot
$addin = Join-Path $root 'dist\ArcAI-Assistant.esriaddin'
$guid  = '{6f2c9d41-8b7a-4c3e-9f10-2d5a7e4b6c81}'
$base  = Join-Path $env:USERPROFILE 'Documents\ArcGIS\AddIns\Desktop10.8'
$dest  = Join-Path $base $guid

if (-not (Test-Path $addin)) { throw "Not found: $addin. Run .\build.ps1 first." }
if (Get-Process ArcMap -ErrorAction SilentlyContinue) {
    throw 'ArcMap is running. Close it and run .\install.ps1 again (the add-in DLL is locked while ArcMap is open).'
}

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force $dest | Out-Null

# 1) Extract config.xml + Images + Install next to the package. Without this the
#    add-in is ignored silently (the Add-In Manager needs the extracted DLL).
$tmpZip = Join-Path $env:TEMP ('arcai-install-' + [guid]::NewGuid().ToString('N') + '.zip')
Copy-Item $addin $tmpZip -Force
try {
    Expand-Archive -Path $tmpZip -DestinationPath $dest -Force
} finally {
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
}

# 2) Keep the package itself in the GUID folder too.
Copy-Item $addin (Join-Path $dest 'ArcAI-Assistant.esriaddin') -Force

# 3) Files extracted from an internet-downloaded ZIP may carry the Mark-of-the-Web;
#    ArcMap refuses to load those without a clear reason.
Get-ChildItem $dest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# 4) Drop a stale assembly cache for this add-in, if any.
$cache = Join-Path $env:LOCALAPPDATA ('ESRI\Desktop10.8\AssemblyCache\' + $guid.ToUpper())
if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }

# 5) Install the companion Python Add-In used by the "内运行" (in-process) mode.
$inprocAddin = Join-Path $root 'dist\ArcAI-InProc.esriaddin'
$inprocGuid  = '{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}'
$inprocDest  = Join-Path $base $inprocGuid
if (Test-Path $inprocAddin) {
    if (Test-Path $inprocDest) { Remove-Item $inprocDest -Recurse -Force }
    New-Item -ItemType Directory -Force $inprocDest | Out-Null
    $tmpInproc = Join-Path $env:TEMP ('arcai-inproc-install-' + [guid]::NewGuid().ToString('N') + '.zip')
    Copy-Item $inprocAddin $tmpInproc -Force
    try {
        Expand-Archive -Path $tmpInproc -DestinationPath $inprocDest -Force
    } finally {
        Remove-Item $tmpInproc -Force -ErrorAction SilentlyContinue
    }
    Copy-Item $inprocAddin (Join-Path $inprocDest 'ArcAI-InProc.esriaddin') -Force
    Get-ChildItem $inprocDest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
} else {
    Write-Warning 'ArcAI-InProc.esriaddin not found; 内运行 will need a manual Python Add-In install.'
}

Write-Host ('Installed to ' + $dest)
Write-Host ('Installed InProc to ' + $inprocDest)
Write-Host 'Restart ArcMap. The ArcAI toolbar and ArcAI InProc toolbar should appear.'
