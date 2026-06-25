# Builds CatCursor.exe. Every colour theme (static + animated cursors), the app
# icon and per-colour previews are packed into a zip and embedded as a managed
# resource, so the .exe is one self-contained file with no install needed.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path "$PSScriptRoot\build")) { throw "build\ folder missing - run 'python make_cat_cursor.py' first." }
if (-not (Test-Path "$PSScriptRoot\build\icon.ico")) { throw "build\icon.ico missing - run 'python make_cat_cursor.py' first." }

# Pack build\* (per-colour folders, previews, icon) into cursors.zip.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = "$PSScriptRoot\cursors.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    "$PSScriptRoot\build", $zip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

# The template is already valid C#; copy it as the compile unit.
Copy-Item "$PSScriptRoot\CatCursor.template.cs" "$PSScriptRoot\CatCursor.cs" -Force

& $csc /nologo /target:winexe /out:"$PSScriptRoot\CatCursor.exe" `
    /win32icon:"$PSScriptRoot\build\icon.ico" `
    /win32manifest:"$PSScriptRoot\app.manifest" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll `
    /resource:"$zip,CatCursor.cursors.zip" `
    "$PSScriptRoot\CatCursor.cs"

if (Test-Path "$PSScriptRoot\CatCursor.exe") {
    $kb = [math]::Round((Get-Item "$PSScriptRoot\CatCursor.exe").Length / 1KB)
    Write-Host "Built CatCursor.exe ($kb KB)."
} else {
    throw "Build failed."
}
