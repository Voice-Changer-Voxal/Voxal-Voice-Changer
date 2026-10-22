# VybeShift - Build & Package Script
# Run: Right-click -> Run with PowerShell (or: powershell -ExecutionPolicy Bypass -File build.ps1)

$ErrorActionPreference = "Stop"
$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$iscc   = ""

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "       VybeShift - Build and Package        " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# -- Step 1: Check .NET 8 SDK ---------------------------------
Write-Host "[1/4] Checking .NET 8 SDK..." -ForegroundColor Yellow

if (-not (Test-Path $dotnet)) {
    Write-Host "  MISSING: .NET SDK not installed." -ForegroundColor Red
    Write-Host "  Opening download page..." -ForegroundColor White
    Start-Process "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    Write-Host "  --> Install .NET 8 SDK (x64), then re-run this script." -ForegroundColor Cyan
    Read-Host "Press Enter to exit"
    exit 1
}

$sdks = & $dotnet --list-sdks 2>&1
if ($sdks -notmatch "8\.") {
    Write-Host "  MISSING: .NET 8 SDK not found." -ForegroundColor Red
    Write-Host "  Installed SDKs: $sdks" -ForegroundColor Gray
    Write-Host "  Opening download page..." -ForegroundColor White
    Start-Process "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "  OK: .NET 8 SDK found" -ForegroundColor Green

# -- Step 2: Check Inno Setup ---------------------------------
Write-Host "[2/4] Checking Inno Setup 6..." -ForegroundColor Yellow

$innoPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
foreach ($p in $innoPaths) { if (Test-Path $p) { $iscc = $p; break } }

if (-not $iscc) {
    Write-Host "  MISSING: Inno Setup 6 not installed." -ForegroundColor Red
    Write-Host "  Opening download page..." -ForegroundColor White
    Start-Process "https://jrsoftware.org/isdl.php"
    Write-Host "  --> Install Inno Setup 6, then re-run this script." -ForegroundColor Cyan
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "  OK: Inno Setup found at: $iscc" -ForegroundColor Green

# -- Step 3: Check VB-Cable -----------------------------------
Write-Host "[3/4] Checking assets..." -ForegroundColor Yellow

$vbCable = Join-Path $root "installer\drivers\VBCABLE_Setup_x64.exe"
if (Test-Path $vbCable) {
    Write-Host "  OK: VB-Cable bundled" -ForegroundColor Green
} else {
    Write-Host "  WARN: VB-Cable not found in installer\drivers\" -ForegroundColor Yellow
    Write-Host "        Users will be told to download it manually." -ForegroundColor Gray
}

$mp3 = Join-Path $root "installer\assets\Digital_Genesis.mp3"
if (Test-Path $mp3) {
    Write-Host "  OK: Installer music found" -ForegroundColor Green
} else {
    Write-Host "  WARN: Digital_Genesis.mp3 not found in installer\assets\" -ForegroundColor Yellow
}

$logo = Join-Path $root "installer\assets\logo.png"
if (Test-Path $logo) {
    Write-Host "  OK: Logo found" -ForegroundColor Green
} else {
    Write-Host "  WARN: logo.png not found in installer\assets\" -ForegroundColor Yellow
}

# -- Step 4: Publish app --------------------------------------
Write-Host "[4/4] Publishing VybeShift (self-contained single EXE)..." -ForegroundColor Yellow

$publishDir = Join-Path $root "VybeShift.UI\bin\publish"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

$projFile = Join-Path $root "VybeShift.UI\VybeShift.UI.csproj"

& $dotnet publish $projFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $publishDir `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  ERROR: Publish failed. See errors above." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "  OK: Published to: $publishDir" -ForegroundColor Green

# -- Step 5: Build installer ----------------------------------
Write-Host ""
Write-Host "[5/5] Building installer EXE..." -ForegroundColor Yellow

$outDir = Join-Path $root "installer\Output"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$issFile = Join-Path $root "installer\setup.iss"
& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Inno Setup build failed." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# -- Done -----------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE!" -ForegroundColor Green
Write-Host ""
Write-Host "  Installer: installer\Output\VybeShift_Setup_v1.0.0.exe" -ForegroundColor Green
Write-Host "  Share this EXE with anyone - they just double-click." -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

Start-Process explorer.exe -ArgumentList $outDir
Read-Host "Press Enter to exit"
