param (
    [string]$Version = "1.1.0"
)

$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
Set-Location $root

Write-Host "=== DevPurge v$Version Release Builder ===" -ForegroundColor Cyan

# 1. Clean previous release artifacts
$releaseDir = Join-Path $root "release"
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}
Get-ChildItem -Path $releaseDir -Filter "DevPurge-v$Version-*" | Remove-Item -Force

$publishDir = Join-Path $root "publish"

# 2. Build Windows x64 GUI
Write-Host "`n[1/5] Publishing Windows x64 GUI (Single-File)..." -ForegroundColor Yellow
$guiOut = Join-Path $publishDir "win-x64-gui"
dotnet publish "$root/src/DevPurge.App/DevPurge.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $guiOut
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }

# 3. Build Windows x64 CLI
Write-Host "`n[2/5] Publishing Windows x64 CLI (Single-File)..." -ForegroundColor Yellow
$winCliOut = Join-Path $publishDir "win-x64-cli"
dotnet publish "$root/src/DevPurge.Cli/DevPurge.Cli.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o $winCliOut
if ($LASTEXITCODE -ne 0) { throw "Windows CLI publish failed." }

# 4. Build Linux x64 CLI
Write-Host "`n[3/5] Publishing Linux x64 CLI (Single-File)..." -ForegroundColor Yellow
$linuxX64Out = Join-Path $publishDir "linux-x64-cli"
dotnet publish "$root/src/DevPurge.Cli/DevPurge.Cli.csproj" -c Release -r linux-x64 --self-contained true `
    -p:PublishSingleFile=true -o $linuxX64Out
if ($LASTEXITCODE -ne 0) { throw "Linux x64 CLI publish failed." }

# 5. Build Linux arm64 CLI
Write-Host "`n[4/5] Publishing Linux ARM64 CLI (Single-File)..." -ForegroundColor Yellow
$linuxArmOut = Join-Path $publishDir "linux-arm64-cli"
dotnet publish "$root/src/DevPurge.Cli/DevPurge.Cli.csproj" -c Release -r linux-arm64 --self-contained true `
    -p:PublishSingleFile=true -o $linuxArmOut
if ($LASTEXITCODE -ne 0) { throw "Linux ARM64 CLI publish failed." }

# 6. Build Inno Setup Installer
Write-Host "`n[5/5] Compiling Inno Setup Windows Installer..." -ForegroundColor Yellow
$isccPaths = @(
    "C:\Users\$env:USERNAME\AppData\Local\Programs\Inno Setup 6\iscc.exe",
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
    "C:\Program Files\Inno Setup 6\iscc.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if ($iscc) {
    Write-Host "Found ISCC compiler: $iscc" -ForegroundColor Green
    & $iscc "$root/installer/DevPurge.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
} else {
    throw "iscc.exe not found! Please ensure Inno Setup 6 is installed."
}

# 7. Package Portable ZIP and TAR.GZ archives
Write-Host "`nPackaging distribution archives..." -ForegroundColor Cyan

# Windows GUI ZIP
$guiZip = Join-Path $releaseDir "DevPurge-v$Version-windows-x64-gui.zip"
if (Test-Path $guiZip) { Remove-Item $guiZip -Force }
Compress-Archive -Path "$guiOut\DevPurge.App.exe", "$root\assets\icon.ico", "$root\README.md", "$root\LICENSE" -DestinationPath $guiZip

# Windows CLI ZIP
$cliZip = Join-Path $releaseDir "DevPurge-v$Version-windows-x64-cli.zip"
if (Test-Path $cliZip) { Remove-Item $cliZip -Force }
Compress-Archive -Path "$winCliOut\DevPurge.Cli.exe", "$root\assets\icon.ico", "$root\README.md", "$root\LICENSE" -DestinationPath $cliZip

# Linux x64 tar.gz
$linuxX64Tar = Join-Path $releaseDir "DevPurge-v$Version-linux-x64-cli.tar.gz"
tar -czf $linuxX64Tar -C $linuxX64Out DevPurge.Cli

# Linux ARM64 tar.gz
$linuxArmTar = Join-Path $releaseDir "DevPurge-v$Version-linux-arm64-cli.tar.gz"
tar -czf $linuxArmTar -C $linuxArmOut DevPurge.Cli

# 8. Compute Checksums
Write-Host "`nGenerating SHA-256 Checksums..." -ForegroundColor Cyan
$checksumFile = Join-Path $releaseDir "checksums.txt"
$files = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -like "DevPurge-v$Version-*" }
$hashes = foreach ($f in $files) {
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
    "$hash  $($f.Name)"
}
$hashes | Out-File -FilePath $checksumFile -Encoding utf8

Write-Host "`n=== All Release Assets Successfully Built! ===" -ForegroundColor Green
$files | Select-Object Name, Length | Format-Table -AutoSize
Get-Content $checksumFile
