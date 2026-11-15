<#
.SYNOPSIS
    Đóng gói HihiBooth thành thư mục chạy được + file zip để cài lên máy kiosk.

.DESCRIPTION
    1. Build UI (ui/ -> ui/dist)
    2. dotnet publish PhotoBooth.Host và PhotoBooth.Watchdog (Release, win-x64, self-contained: máy kiosk không cần cài .NET)
    3. Chép kèm ui/dist, resources/scripts, Canon EDSDK.dll
    4. Nén thành publish/HihiBooth-<version>-win-x64.zip

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 -Version 1.0.0
#>
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    [switch]$SkipUi,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root "publish"
$appDir = Join-Path $publishRoot "HihiBooth"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Invoke-Checked([string]$what, [scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what thất bại (exit code $LASTEXITCODE)" }
}

# Host đang chạy sẽ khoá file
if (Get-Process PhotoBooth.Host, PhotoBooth.Watchdog -ErrorAction SilentlyContinue) {
    throw "PhotoBooth.Host / Watchdog đang chạy. Hãy tắt app trước khi đóng gói."
}

# 1. UI
if (-not $SkipUi) {
    Step "Build UI"
    Push-Location (Join-Path $root "ui")
    try {
        if (-not (Test-Path "node_modules")) { Invoke-Checked "npm ci" { npm ci } }
        Invoke-Checked "npm run build" { npm run build }
    } finally { Pop-Location }
}
$uiDist = Join-Path $root "ui\dist"
if (-not (Test-Path (Join-Path $uiDist "index.html"))) { throw "Không thấy ui/dist/index.html. Chạy lại không có -SkipUi." }

# Canon EDSDK (không commit trong repo)
$edsdk = Join-Path $root "backend\lib\EDSDK\EDSDK.dll"
if (-not (Test-Path $edsdk)) {
    Write-Warning "Thiếu backend/lib/EDSDK/EDSDK.dll: bản đóng gói sẽ không điều khiển được máy ảnh Canon."
}
# FUJIFILM X SDK (không commit trong repo); csproj tự chép XAPI.dll, FF*.dll, XSDK.DAT khi publish
if (-not (Test-Path (Join-Path $root "backend\lib\FujiXSDK\XAPI.dll"))) {
    Write-Warning "Thiếu backend/lib/FujiXSDK/XAPI.dll: bản đóng gói sẽ không điều khiển được máy ảnh Fujifilm."
}

# 2. Publish .NET
Step "Dọn thư mục $appDir"
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
New-Item -ItemType Directory -Force $appDir | Out-Null

$common = @("-c", "Release", "-r", $Runtime, "--self-contained", "true", "-p:Version=$Version", "-p:DebugType=none", "-o", $appDir, "--nologo")

Step "Publish PhotoBooth.Host"
Invoke-Checked "publish Host" { dotnet publish (Join-Path $root "backend\src\PhotoBooth.Host\PhotoBooth.Host.csproj") @common }

Step "Publish PhotoBooth.Watchdog"
Invoke-Checked "publish Watchdog" { dotnet publish (Join-Path $root "backend\src\PhotoBooth.Watchdog\PhotoBooth.Watchdog.csproj") @common }

# 3. File đi kèm
Step "Chép UI, scripts, EDSDK"
$uiTarget = Join-Path $appDir "ui\dist"
New-Item -ItemType Directory -Force $uiTarget | Out-Null
Copy-Item (Join-Path $uiDist "*") $uiTarget -Recurse -Force

$scriptsTarget = Join-Path $appDir "resources\scripts"
New-Item -ItemType Directory -Force $scriptsTarget | Out-Null
Copy-Item (Join-Path $root "resources\scripts\*") $scriptsTarget -Recurse -Force

if (Test-Path $edsdk) { Copy-Item $edsdk $appDir -Force }

@"
HihiBooth $Version ($Runtime)
================================

Chạy app:
  - Máy kiosk: chạy PhotoBooth.Watchdog.exe (tự mở lại PhotoBooth.Host.exe nếu app bị treo / thoát).
  - Chạy thử: mở trực tiếp PhotoBooth.Host.exe.

Yêu cầu máy:
  - Windows 10/11 64-bit. Không cần cài .NET.
  - Microsoft Edge WebView2 Runtime (Windows 11 có sẵn; Windows 10 cũ tải tại https://developer.microsoft.com/microsoft-edge/webview2/).

Máy ảnh (app tự nhận máy đang cắm; không có máy thì dùng webcam):
  - Canon (EOS R50...): EDSDK.dll cùng thư mục với PhotoBooth.Host.exe. Tắt EOS Utility / EOS Webcam Utility,
    bật máy ở chế độ chụp ảnh, mở ống kính (nếu là ống thu gọn).
  - Fujifilm X (X-T, X-S...): XAPI.dll, XSDK.DAT, FTLPTP*.dll, FF00xxAPI.dll cùng thư mục.
    Trên máy: Kết nối > Chế độ USB = USB TETHER SHOOTING AUTO; chất lượng ảnh JPEG hoặc RAW+JPEG.
    Tắt X Acquire / FUJIFILM X Webcam.
  - Kiểm tra trong Admin > Kiểm Tra Thiết Bị.

Video timelapse: bật/tắt trong Admin > Thời Gian; lưu timelapse.mp4 trong thư mục ảnh của từng phiên.

Dữ liệu:
  - Log, cache CMS, phiên chụp: C:\ProgramData\HihiBooth\Data
  - Ảnh khách: D:\HihiBooth_Gallery (hoặc Pictures\HihiBooth_Gallery nếu máy không có ổ D)
"@ | Set-Content -Path (Join-Path $appDir "HUONG_DAN.txt") -Encoding UTF8

# 4. Zip
if (-not $NoZip) {
    $zip = Join-Path $publishRoot "HihiBooth-$Version-$Runtime.zip"
    Step "Nén $zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
}

$size = (Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Step ("Xong: {0} ({1:N0} MB)" -f $appDir, $size)
