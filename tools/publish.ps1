# Buduje single-file .exe (standard TMA, filar 5) i zapisuje version.txt zgodne z wydaniem.
# Uruchom: powershell -File tools/publish.ps1 -Version 1.1
param([string]$Version = "1.1")
$ErrorActionPreference = "Stop"

dotnet publish src/WifiTester.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist

# UWAGA: bez BOM — Set-Content -Encoding utf8 (PS 5.1) dodaje BOM, co psuje parser wersji.
[System.IO.File]::WriteAllText((Join-Path (Get-Location) "version.txt"), $Version, (New-Object System.Text.UTF8Encoding $false))

$exe = "dist\WifiTester.App.exe"
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 0)
Write-Output "Opublikowano $exe (v$Version, ${mb} MB). version.txt = $Version"
Write-Output "Następnie: gh release create v$Version -R TMA-Automation/wifi-tester --title `"WifiTester v$Version`" $exe"
