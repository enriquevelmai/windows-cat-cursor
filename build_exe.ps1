# Builds CatCursor.exe. Every colour theme (static + animated cursors) is packed
# into a zip and embedded as a managed resource, so the .exe is one self-contained
# file with no install needed.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path "$PSScriptRoot\build")) { throw "build\ folder missing - run 'python make_cat_cursor.py' first." }

# Pack build\<Colour>\*.cur|*.ani into cursors.zip with entries like "Orange/cat_cursor.cur".
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = "$PSScriptRoot\cursors.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    "$PSScriptRoot\build", $zip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

# Inject the (small) preview image used for the window logo.
$previewB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("$PSScriptRoot\cat_preview_128.png"))
$src = Get-Content "$PSScriptRoot\CatCursor.template.cs" -Raw
$src = $src.Replace('__PREVIEW_BASE64__', $previewB64)
Set-Content "$PSScriptRoot\CatCursor.cs" -Value $src -Encoding UTF8

& $csc /nologo /target:winexe /out:"$PSScriptRoot\CatCursor.exe" `
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
