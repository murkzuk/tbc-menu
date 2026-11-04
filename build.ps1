# Builds the mod and packs dist/TBC_Menu-<version>.zip
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root 'src\TBC_Menu\TBC_Menu.csproj'

if (-not (Test-Path (Join-Path $root 'game.path')) -and
    -not (Test-Path (Join-Path $root 'Local.props')) -and
    -not $env:TBC_GAME_DIR) {
    Write-Host 'No game.path / Local.props / TBC_GAME_DIR found - trying tools/find-game.ps1'
    & (Join-Path $root 'tools\find-game.ps1')
}

Write-Host ('Building ' + $Configuration + ' ...')
& dotnet build $csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$propsText = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = ([regex]::Match($propsText, '<Version>([^<]+)</Version>')).Groups[1].Value

$dist = Join-Path $root 'dist'
$staging = Join-Path $dist 'TBC_Menu'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item (Join-Path $root ('src\TBC_Menu\bin\' + $Configuration + '\TBC_Menu.dll')) $staging
Copy-Item (Join-Path $root 'README.md') $staging
Copy-Item (Join-Path $root 'LICENSE') $staging

$zip = Join-Path $dist ('TBC_Menu-' + $version + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Write-Host ('Wrote ' + $zip)
