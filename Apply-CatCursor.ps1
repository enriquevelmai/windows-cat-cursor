# Applies a cat cursor theme in the chosen colour (current user only).
#   .\Apply-CatCursor.ps1                 # Orange (default)
#   .\Apply-CatCursor.ps1 -Color Black
# Colours: Orange Black Grey White Siamese.  Undo with Revert-CatCursor.ps1.

param([ValidateSet('Orange','Black','Grey','White','Siamese')] [string] $Color = 'Orange')

$ErrorActionPreference = 'Stop'

$srcDir = Join-Path $PSScriptRoot "build\$Color"
if (-not (Test-Path $srcDir)) { throw "build\$Color not found - run 'python make_cat_cursor.py' first." }

# registry role -> file (Wait/AppStarting are animated .ani)
$map = [ordered]@{
    Arrow='cat_cursor.cur'; Hand='cat_paw.cur'; Help='cat_help.cur';
    AppStarting='cat_working.ani'; Wait='cat_busy.ani'; IBeam='cat_text.cur';
    Crosshair='cat_cross.cur'; No='cat_no.cur'; SizeNS='cat_ns.cur';
    SizeWE='cat_we.cur'; SizeNWSE='cat_nwse.cur'; SizeNESW='cat_nesw.cur';
    SizeAll='cat_move.cur'; NWPen='cat_pen.cur'; UpArrow='cat_up.cur'
}

$key = 'HKCU:\Control Panel\Cursors'
foreach ($role in $map.Keys) {
    $file = Join-Path $srcDir $map[$role]
    if (-not (Test-Path $file)) { throw "$($map[$role]) missing in $srcDir." }
    Set-ItemProperty -Path $key -Name $role -Value $file
}
Set-ItemProperty -Path $key -Name '(Default)' -Value "Cat Cursor ($Color)"

Add-Type -Namespace Win32 -Name Cur -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.IntPtr pvParam, uint fWinIni);
'@
[Win32.Cur]::SystemParametersInfo(0x57, 0, [System.IntPtr]::Zero, 0x03) | Out-Null

Write-Host "$Color cat cursor theme applied. (Run Revert-CatCursor.ps1 to undo.)"
