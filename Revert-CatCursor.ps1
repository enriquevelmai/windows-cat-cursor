# Restores the default Windows cursors (current user only).

$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Control Panel\Cursors'

$roles = 'Arrow','Hand','Help','AppStarting','Wait','IBeam','Crosshair','No',
         'SizeNS','SizeWE','SizeNWSE','SizeNESW','SizeAll','NWPen','UpArrow'

# Clearing each value makes Windows fall back to the system default.
foreach ($role in $roles) { Set-ItemProperty -Path $key -Name $role -Value '' }
Set-ItemProperty -Path $key -Name '(Default)' -Value 'Windows Default'

Add-Type -Namespace Win32 -Name CurR -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.IntPtr pvParam, uint fWinIni);
'@
[Win32.CurR]::SystemParametersInfo(0x57, 0, [System.IntPtr]::Zero, 0x03) | Out-Null

Write-Host "Default cursors restored."
