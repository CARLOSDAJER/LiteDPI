<#
.SYNOPSIS
    Installs (or updates) LiteDPI for the current user. No admin rights needed.

.DESCRIPTION
    - Run from an unzipped release folder: installs the files next to this script.
    - Run from a git clone (no LiteDPI.exe yet): builds from source first.
    - Run straight from the web: downloads the latest release from GitHub.

        irm https://raw.githubusercontent.com/CARLOSDAJER/LiteDPI/main/install.ps1 | iex

    Installs to %LOCALAPPDATA%\Programs\LiteDPI, adds a Start menu shortcut, an entry in
    Settings > Apps (for uninstalling), turns on "Start with Windows", and starts LiteDPI.
    Your LiteDPI.ini settings are kept when updating.

.PARAMETER NoStartup
    Don't start LiteDPI automatically with Windows.
#>
param(
    [switch]$NoStartup,
    [string]$Repo = 'CARLOSDAJER/LiteDPI'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$dest     = Join-Path $env:LOCALAPPDATA 'Programs\LiteDPI'
$exe      = Join-Path $dest 'LiteDPI.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'LiteDPI.lnk'
$runKey   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$appsKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LiteDPI'
$files    = 'LiteDPI.exe', 'litedpi-probe.exe', 'uninstall.ps1', 'README.md'

function Step($msg) { Write-Host "  $msg" }

Write-Host ''
Write-Host 'Installing LiteDPI' -ForegroundColor Cyan

# 1. Find the files to install.
$here = $PSScriptRoot
$temp = $null
if ($here -and (Test-Path (Join-Path $here 'LiteDPI.exe'))) {
    $src = $here
    Step "Using files in $src"
}
elseif ($here -and (Test-Path (Join-Path $here 'build.cmd'))) {
    Step 'Building from source...'
    & cmd /c "`"$(Join-Path $here 'build.cmd')`"" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Run build.cmd to see the error.' }
    $src = $here
}
else {
    Step "Downloading the latest release of $Repo..."
    $release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'LiteDPI-installer' }
    $asset = $release.assets | Where-Object { $_.name -like 'LiteDPI-*.zip' } | Select-Object -First 1
    if (-not $asset) { throw "No LiteDPI zip found in the latest release of $Repo." }
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('LiteDPI-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $temp | Out-Null
    $zip = Join-Path $temp $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath (Join-Path $temp 'x')
    $src = Join-Path $temp 'x'
    Step "Got $($release.tag_name)"
}

# 2. Stop a running copy so its files can be replaced.
Get-Process LiteDPI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# 3. Copy files (LiteDPI.ini is left alone so settings survive updates).
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($f in $files) {
    $from = Join-Path $src $f
    if (Test-Path $from) { Copy-Item $from $dest -Force }
}
Get-ChildItem $dest -File | Unblock-File
Step "Installed to $dest"

# 4. Start menu shortcut.
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Description = 'Per-game mouse DPI'
$lnk.Save()
Step 'Added Start menu shortcut'

# 5. Start with Windows.
if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name 'LiteDPI' -ErrorAction SilentlyContinue
}
else {
    Set-ItemProperty -Path $runKey -Name 'LiteDPI' -Value "`"$exe`""
    Step 'Will start with Windows'
}

# 6. Settings > Apps entry, so it can be uninstalled like any other app.
$version = (Get-Item $exe).VersionInfo.ProductVersion
New-Item -Path $appsKey -Force | Out-Null
$uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $dest 'uninstall.ps1')`""
$props = @{
    DisplayName     = 'LiteDPI'
    DisplayVersion  = $version
    Publisher       = 'LiteDPI'
    DisplayIcon     = $exe
    InstallLocation = $dest
    UninstallString = $uninstall
    URLInfoAbout    = "https://github.com/$Repo"
}
foreach ($k in $props.Keys) { Set-ItemProperty -Path $appsKey -Name $k -Value $props[$k] }
Set-ItemProperty -Path $appsKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $appsKey -Name 'NoRepair' -Value 1 -Type DWord

if ($temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }

# 7. Start it.
Start-Process $exe
Write-Host ''
Write-Host "LiteDPI $version is running. Look for the small coloured square in the system tray." -ForegroundColor Green
if (Get-Process lghub, lghub_agent -ErrorAction SilentlyContinue) {
    Write-Host 'Note: G HUB is running and will override LiteDPI. Quit it from its tray icon.' -ForegroundColor Yellow
}
Write-Host ''
