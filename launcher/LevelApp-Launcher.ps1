# LevelApp launcher: installs/updates the latest GitHub release into a fixed folder, then starts the app.
# Link LevelApp.cmd (next to this file) from the desktop. Contains no version number.
$ErrorActionPreference = 'Stop'
$repoApi    = 'https://api.github.com/repos/soldernerd/LevelApp/releases/latest'
$installDir = Join-Path $env:LOCALAPPDATA 'LevelApp\app'
$exe        = Join-Path $installDir 'LevelApp.App.exe'
$verFile    = Join-Path $installDir 'installed-version.txt'

function Get-InstalledVersion {
    if ((Test-Path $exe) -and (Test-Path $verFile)) { return (Get-Content $verFile -Raw).Trim() }
    return $null
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $rel    = Invoke-RestMethod -Uri $repoApi -Headers @{ 'User-Agent' = 'LevelApp-Launcher' } -TimeoutSec 15
    $latest = $rel.tag_name.TrimStart('v')
    $asset  = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) { throw 'Latest release has no .zip asset.' }

    $installed = Get-InstalledVersion
    if ($installed -ne $latest) {
        Write-Host "Updating LevelApp: $installed -> $latest ..."
        # Make sure no running copy locks the files.
        Get-Process -Name 'LevelApp.App' -ErrorAction SilentlyContinue | ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(10000)) { $_.Kill(); $_.WaitForExit() }
        }
        $zip     = Join-Path $env:TEMP "LevelApp-launcher-$latest.zip"
        $staging = Join-Path $env:TEMP "LevelApp-launcher-$latest"
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers @{ 'User-Agent' = 'LevelApp-Launcher' } -TimeoutSec 300
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $staging -Force
        if (-not (Test-Path (Join-Path $staging 'LevelApp.App.exe'))) { throw 'Downloaded zip does not contain LevelApp.App.exe.' }

        if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        Copy-Item -Path (Join-Path $staging '*') -Destination $installDir -Recurse -Force
        Set-Content -Path $verFile -Value $latest -NoNewline
        Remove-Item $zip, $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
catch {
    Write-Warning "Update check failed: $($_.Exception.Message)"
    if (-not (Test-Path $exe)) {
        Write-Host 'No installed version available. Press Enter to close.'
        [void][Console]::ReadLine()
        exit 1
    }
    Write-Host 'Starting the installed version instead.'
    Start-Sleep -Seconds 2
}

Start-Process -FilePath $exe -WorkingDirectory $installDir
