#requires -Version 7
<#
.SYNOPSIS
    Chụp cửa sổ chính của app ra PNG để đính vào PR (#133), với dữ liệu trống.

.DESCRIPTION
    Chạy PdfReaderApp với TRITHU_DATA_DIR trỏ tới thư mục tạm (không đụng thư viện thật) và
    TRITHU_SCREENSHOT: app tự render cửa sổ chính bằng RenderTargetBitmap, lưu PNG rồi thoát.
    Không chụp màn hình nên không lộ cửa sổ khác trên desktop.

    Cảnh chụp tuỳ chọn: -Open mở một file PDF (-Open sample dùng scripts/assets/snapshot-sample.pdf),
    -Page, -Zoom, -Panel chat|notes, -Theme light|dark. Mỗi lần chạy chụp một ảnh.

.EXAMPLE
    pwsh scripts/snapshot.ps1 -Out artifacts/main.png
.EXAMPLE
    pwsh scripts/snapshot.ps1 -Out artifacts/notes.png -Open sample -Page 2 -Zoom 1.2 -Panel notes
.EXAMPLE
    pwsh scripts/snapshot.ps1 -Out artifacts/dark.png -Open sample -Page 2 -Zoom 1.2 -Theme dark
#>
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Exe,
    [int]$DelaySeconds = 3,
    [string]$Open,
    [int]$Page,
    [double]$Zoom,
    [ValidateSet('', 'chat', 'notes')][string]$Panel = '',
    [ValidateSet('light', 'dark')][string]$Theme = 'light'
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
$dataDir = Join-Path $profile 'Data'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
# App chỉ có lựa chọn theme trong UI; ghi sẵn file mà WindowsSettingsService đọc lúc khởi động.
$themeValue = if ($Theme -eq 'dark') { 'Dark' } else { 'Light' }
Set-Content -LiteralPath (Join-Path $dataDir 'theme.pref') -Value $themeValue -NoNewline
try {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $profile
    $info.Environment['TRITHU_DATA_DIR'] = $dataDir
    $info.Environment['TRITHU_SCREENSHOT'] = $Out
    $info.Environment['TRITHU_SCREENSHOT_DELAY'] = "$DelaySeconds"
    if ($Open) {
        $pdf = if ($Open -eq 'sample') { Join-Path $repoRoot 'scripts/assets/snapshot-sample.pdf' } else { $Open }
        $info.Environment['TRITHU_SCREENSHOT_OPEN'] = (Resolve-Path $pdf).Path
    }
    if ($Page -gt 0) { $info.Environment['TRITHU_SCREENSHOT_PAGE'] = "$Page" }
    if ($Zoom -gt 0) { $info.Environment['TRITHU_SCREENSHOT_ZOOM'] = $Zoom.ToString([Globalization.CultureInfo]::InvariantCulture) }
    if ($Panel) { $info.Environment['TRITHU_SCREENSHOT_PANEL'] = $Panel }
    # App là WinExe, không có console: phải redirect thì mới đọc được lỗi nó ghi ra stderr.
    $info.RedirectStandardError = $true
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($info)
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(90000)) {
        $process.Kill($true)
        throw 'App không tự thoát sau 90 giây.'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path $Out)) {
        throw "App thoát (mã $($process.ExitCode)) nhưng không có ảnh.`n$($stderr.Result.Trim())"
    }
    Write-Output $Out
}
finally {
    Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
}
