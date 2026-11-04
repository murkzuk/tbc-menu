# Writes game.path at the repo root by scanning Steam libraries.
# Usage:  powershell -ExecutionPolicy Bypass -File tools/find-game.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$title = 'Tank Battle Classic'
$exe = 'Tank Battle Classic.exe'

$steamRoots = @()
foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
    foreach ($name in @('SteamPath', 'InstallPath')) {
        $v = (Get-ItemProperty $key -Name $name -ErrorAction SilentlyContinue).$name
        if ($v) { $steamRoots += ($v -replace '/', '\') }
    }
}

$libraries = @()
foreach ($s in ($steamRoots | Select-Object -Unique)) {
    $libraries += $s
    $vdf = Join-Path $s 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $text = Get-Content $vdf -Raw
        foreach ($m in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
            $libraries += ($m.Groups[1].Value -replace '\\\\', '\')
        }
    }
}

foreach ($lib in ($libraries | Select-Object -Unique)) {
    $candidate = Join-Path $lib ('steamapps\common\' + $title)
    if (Test-Path (Join-Path $candidate $exe)) {
        Set-Content -Path (Join-Path $root 'game.path') -Value $candidate -Encoding ASCII
        Write-Host "Found game at: $candidate"
        Write-Host "Wrote game.path"
        exit 0
    }
}

Write-Host "Could not find '$title'. Launch Steam once, or create game.path manually with the folder that contains $exe." -ForegroundColor Yellow
exit 1
