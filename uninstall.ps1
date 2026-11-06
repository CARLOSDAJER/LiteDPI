<#
.SYNOPSIS
    Removes LiteDPI for the current user: the app, its settings, the Start menu shortcut,
    the "Start with Windows" entry and the Settings > Apps entry.
    Settings > Apps > LiteDPI > Uninstall runs this script.
#>
$ErrorActionPreference = 'Continue'

$dest     = Join-Path $env:LOCALAPPDATA 'Programs\LiteDPI'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'LiteDPI.lnk'

Get-Process LiteDPI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LiteDPI' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LiteDPI' -Recurse -ErrorAction SilentlyContinue
Remove-Item $shortcut -ErrorAction SilentlyContinue

# Only ever delete our own install folder.
if ((Split-Path $dest -Leaf) -eq 'LiteDPI' -and (Test-Path $dest)) {
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'LiteDPI has been removed. Your mouse keeps its current speed until you unplug it.'
