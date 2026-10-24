#requires -Version 7
<#
.SYNOPSIS
    Chụp cửa sổ chính của app ra PNG để đính vào PR (#133), với dữ liệu trống.

.DESCRIPTION
    Chạy PdfReaderApp với TRITHU_DATA_DIR trỏ tới thư mục tạm (không đụng thư viện thật) và
    TRITHU_SCREENSHOT: app tự render cửa sổ chính bằng RenderTargetBitmap, lưu PNG rồi thoát.
    Không chụp màn hình nên không lộ cửa sổ khác trên desktop.

.EXAMPLE
    pwsh scripts/snapshot.ps1 -Out artifacts/main.png
#>
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Exe,
    [int]$DelaySeconds = 3
)

$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
if (-not $Exe) {
    $Exe = Join-Path $repoRoot 'src/PdfReaderApp/bin/Debug/net10.0-windows/PdfReaderApp.exe'
}
if (-not (Test-Path $Exe)) { throw "Không thấy $Exe; chạy dotnet build trước." }
$Out = [IO.Path]::GetFullPath($Out)
# Ảnh cũ từ lần chạy trước không được lọt qua như ảnh mới.
Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue

$profile = Join-Path ([IO.Path]::GetTempPath()) "trithu-snap-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Force -Path $profile | Out-Null
try {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $profile
    $info.Environment['TRITHU_DATA_DIR'] = Join-Path $profile 'Data'
    $info.Environment['TRITHU_SCREENSHOT'] = $Out
    $info.Environment['TRITHU_SCREENSHOT_DELAY'] = "$DelaySeconds"
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(90000)) {
        $process.Kill($true)
        throw 'App không tự thoát sau 90 giây.'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path $Out)) { throw "App thoát (mã $($process.ExitCode)) nhưng không có ảnh." }
    Write-Output $Out
}
finally {
    Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
}
