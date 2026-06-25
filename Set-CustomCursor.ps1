<#
.SYNOPSIS
  Turn any picture into a Windows mouse cursor (current user only).

.EXAMPLE
  .\Set-CustomCursor.ps1 -Image "C:\pics\mycat.png"
  .\Set-CustomCursor.ps1 -Image ".\dog.png" -Role Hand -Hotspot TopCenter
  .\Set-CustomCursor.ps1 -Image ".\smiley.png" -Role All -Hotspot Center

.NOTES
  Roles: Arrow Hand Help AppStarting Wait IBeam Crosshair No
         SizeNS SizeWE SizeNWSE SizeNESW SizeAll NWPen UpArrow All
  PNG with a transparent background looks best.
  Undo with Revert-CatCursor.ps1.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Image,
    [string] $Role = 'Arrow',
    [ValidateSet('TopLeft', 'TopCenter', 'Center')] [string] $Hotspot = 'TopLeft'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Image)) { throw "Image not found: $Image" }
$Image = (Resolve-Path $Image).Path

$allRoles = 'Arrow','Hand','Help','AppStarting','Wait','IBeam','Crosshair','No',
            'SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','NWPen','UpArrow'
if ($Role -ne 'All' -and $allRoles -notcontains $Role) {
    throw "Unknown role '$Role'. Valid: $($allRoles -join ' ') All"
}

$hot = switch ($Hotspot) {
    'TopLeft'   { @(0.0, 0.0) }
    'TopCenter' { @(0.5, 0.0) }
    'Center'    { @(0.5, 0.5) }
}

# Compile the shared converter and load it.
Add-Type -Path (Join-Path $PSScriptRoot 'CursorMaker.cs') `
    -ReferencedAssemblies System.Drawing

$dir = Join-Path $env:LOCALAPPDATA 'CatCursor'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$key = 'HKCU:\Control Panel\Cursors'
if ($Role -eq 'All') {
    $out = Join-Path $dir 'custom_all.cur'
    [CursorMaker]::BuildCurFile($Image, $out, [double]$hot[0], [double]$hot[1])
    foreach ($r in $allRoles) { Set-ItemProperty -Path $key -Name $r -Value $out }
    Set-ItemProperty -Path $key -Name '(Default)' -Value 'Custom Cursor'
} else {
    $out = Join-Path $dir "custom_$Role.cur"
    [CursorMaker]::BuildCurFile($Image, $out, [double]$hot[0], [double]$hot[1])
    Set-ItemProperty -Path $key -Name $Role -Value $out
}

Add-Type -Namespace Win32 -Name CurC -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.IntPtr pvParam, uint fWinIni);
'@
[Win32.CurC]::SystemParametersInfo(0x57, 0, [System.IntPtr]::Zero, 0x03) | Out-Null

Write-Host "Applied '$Image' to '$Role' (hotspot: $Hotspot). Undo with Revert-CatCursor.ps1."
