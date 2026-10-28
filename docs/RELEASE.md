# Runbook phát hành Trí Thư

Quy trình đưa một Release lên Microsoft Store (ADR 0005, ADR 0006). Thuật ngữ Release, Package Identity, Submission, MSIX smoke test: xem `CONTEXT.md` mục "Phát hành (Release)".

Thành phần trong repo:

| Thành phần | Vai trò |
|---|---|
| `.github/workflows/release.yml` | Chạy khi push tag `v*.*.*`: `dotnet test` (lớp chặn) → `pack-msix.ps1` → artifact `msix` + GitHub Release `--generate-notes`. |
| `pack-msix.ps1` | Publish self-contained x64 → stamp version `X.Y.Z.0` vào manifest + assembly → `winapp package`. Chạy y hệt ở local (xem cuối trang). |
| `Package.appxmanifest`, `Assets/` | Manifest MSIX (version `0.0.0.0` là placeholder, version thật lấy từ tag) và logo/tile. |
| `LICENSE`, `THIRD-PARTY-NOTICES.md` | AGPL-3.0 (ADR 0006): điều kiện chặn mọi Submission. |

## A. One-time setup

Làm một lần trước Submission đầu tiên (chi tiết: #99). Bước nào chưa xong thì chưa tạo Submission.

- [ ] Đăng ký individual developer account (miễn phí) tại storedeveloper.microsoft.com, xác minh giấy tờ tùy thân + selfie, vào được Partner Center "Apps & Games".
- [ ] Reserve tên app "Trí Thư" trên Partner Center (nếu tên có dấu bị từ chối, chốt phương án dự phòng và cập nhật `DisplayName` trong `Package.appxmanifest`).
- [ ] Lấy Package Identity (Product identity: `Name`, `Publisher`, `PublisherDisplayName`) từ Partner Center, thay các giá trị `Placeholder` trong `Package.appxmanifest`, merge qua PR. Xoá `devcert.pfx` local cũ (Publisher `CN=Placeholder`) để `pack-msix.ps1` sinh lại cert khớp Publisher mới.
- [ ] Thay logo/tile placeholder trong `Assets/` (44x44, 150x150, 310x150, StoreLogo, SplashScreen) bằng bản thật, merge qua PR.
- [ ] Viết `PRIVACY.md` (nội dung tài liệu chỉ gửi tới OpenAI khi người dùng chủ động chat bằng API key của chính họ; app không có server, không thu thập dữ liệu) và publish qua GitHub Pages; mở URL ở trình duyệt chưa đăng nhập để kiểm tra.
- [ ] Nháp Store listing tiếng Việt, mở mọi thị trường: mô tả, screenshots (`pwsh scripts/snapshot.ps1 -Out x.png` cho ảnh cửa sổ chính), age rating questionnaire, privacy URL, khai báo license AGPL-3.0 + link mã nguồn.
- [ ] Properties > Product declarations: tích "This product contains generative AI..." (chat AI tạo nội dung mới). Thiếu ô này thì certification từ chối theo chính sách 11.16 Live Generative AI Content.
- [ ] Cài [winapp CLI](https://github.com/microsoft/winappCli) nếu muốn đóng gói ở local (`winapp --help` chạy được).

Không có bước cài dev cert một lần: CI sinh dev cert mới ở mỗi lần chạy (`devcert.pfx` không nằm trong repo), nên cert được tin ở bước smoke test của từng Release.

## B. Mỗi Release

Làm theo đúng thứ tự. **Fail bước nào thì dừng release**: sửa lỗi qua PR như bình thường, rồi bắt đầu lại từ bước 1 với version mới (không dời tag đã push).

### 1. Chọn version và push tag

- [ ] Chọn version SemVer `X.Y.Z` lớn hơn bản trước (Store bắt version mới lớn hơn bản đã nộp).
- [ ] Từ `main` mới nhất, workflow **Check** xanh:

  ```powershell
  git switch main; git pull
  git tag vX.Y.Z
  git push origin vX.Y.Z
  ```

### 2. Chờ CI và tải MSIX

- [ ] Workflow **Release** của tag xanh (tab Actions). Step `Test` đỏ thì không có MSIX: dừng release.
- [ ] GitHub Release `vX.Y.Z` có file `PdfReaderApp_X.Y.Z.msix` và notes tự tổng hợp từ PR titles. Tải về:

  ```powershell
  gh release download vX.Y.Z --pattern "*.msix"
  ```

### 3. MSIX smoke test (bắt buộc)

Cài đúng file `.msix` vừa tải trên máy thật. Test suite không bắt được lớp lỗi chỉ có trong MSIX (AppData ảo hóa, package identity), nên không bỏ bước này kể cả khi CI xanh.

Chạy trong **Windows PowerShell 5.1** (`powershell.exe`, không phải `pwsh`: các cmdlet Appx hay lỗi trên PowerShell 7) mở bằng quyền **Administrator**, tại gốc repo.

- [ ] Nếu máy đã từng chạy app từ `bin/`, đổi tên `%APPDATA%\PdfReaderApp` tạm thời (vd thành `PdfReaderApp.bak`). Thư mục này có sẵn thì app trong MSIX dùng luôn nó thay vì thư mục ảo hóa, bước kiểm tra dữ liệu bên dưới sẽ sai.
- [ ] Tin cert đã ký package (CI sinh cert mới mỗi lần nên làm lại cho mỗi Release):

  ```powershell
  $sig = Get-AuthenticodeSignature .\PdfReaderApp_X.Y.Z.msix
  $thumb = $sig.SignerCertificate.Thumbprint
  Export-Certificate -Cert $sig.SignerCertificate -FilePath .\release-signer.cer
  Import-Certificate -FilePath .\release-signer.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
  ```

  Cách bằng giao diện (thay cho lệnh trên): chuột phải file `.msix` > Properties > Digital Signatures > Details > View Certificate > Install Certificate > Local Machine > Place all certificates in the following store > Trusted People. Bỏ qua bước này thì App Installer báo `0x800B010A` và nút Install bị mờ.

- [ ] Cài: `Add-AppxPackage .\PdfReaderApp_X.Y.Z.msix` (hoặc nhấp đúp file `.msix`), không báo lỗi.
- [ ] Mở "Trí Thư" từ Start menu: app chạy, không cần cài .NET runtime, chạy được khi chưa có API key (điều kiện certification).
- [ ] Mở một PDF vào Library, mở từ Library: trang render đúng, cuộn và zoom được.
- [ ] Tạo Workspace, thêm Document vào Workspace, mở vài Tab.
- [ ] Nhập OpenAI API key trong Settings, chat AI về tài liệu: có câu trả lời.
- [ ] Đóng app, mở lại: Library, Workspace, Tab và Chat thread còn nguyên. Dữ liệu nằm trong thư mục ảo hóa của package, không ở `%APPDATA%\PdfReaderApp`:

  ```powershell
  $pkg = Get-AppxPackage -Name ([xml](Get-Content .\Package.appxmanifest)).Package.Identity.Name
  Test-Path "$env:LOCALAPPDATA\Packages\$($pkg.PackageFamilyName)\LocalCache\Roaming\PdfReaderApp"   # True
  Test-Path "$env:APPDATA\PdfReaderApp"                                                                # False
  ```

- [ ] Gỡ cài sạch: `$pkg | Remove-AppxPackage`. App biến mất khỏi Start menu và `Test-Path "$env:LOCALAPPDATA\Packages\$($pkg.PackageFamilyName)"` trả về `False`.
- [ ] Dọn: bỏ tin cert của bản này (`Remove-Item "Cert:\LocalMachine\TrustedPeople\$thumb"`, hoặc xoá cert `CN=<Publisher>` trong `certlm.msc` > Trusted People > Certificates), xoá `release-signer.cer`, đổi tên `%APPDATA%\PdfReaderApp` về như cũ nếu đã đổi.

### 4. Tạo Submission trên Partner Center

- [ ] Tạo Submission mới, upload `PdfReaderApp_X.Y.Z.msix` (package không cần ký lại: Store tự re-sign sau certification).
- [ ] Viết tay "What's new" tiếng Việt, 3-5 gạch đầu dòng hướng người dùng (bản mới làm được gì). **Không** dán commit log hay notes của GitHub Release.
- [ ] Listing còn khai báo AGPL-3.0 và link mã nguồn trỏ đúng tag: `https://github.com/jamestran1/PdfReader/tree/vX.Y.Z`.
- [ ] Store listings phải Complete cho đúng ngôn ngữ package khai báo (`vi-VN`). Upload package có thể thêm listing Vietnamese trống: điền description + screenshot, bỏ ngôn ngữ thừa qua "Add/remove languages".
- [ ] Submission options > Restricted capabilities: app WPF đóng gói MSIX luôn cần `runFullTrust`. Ô giải thích giới hạn độ dài, dán bản ngắn:

  ```text
  Trí Thư is a WPF desktop application (.NET 10) packaged as MSIX. As a classic Win32 desktop app, it requires runFullTrust to run outside the AppContainer; this is the standard capability for desktop apps packaged with MSIX.
  ```

### 5. Submit và theo dõi certification

- [ ] Submit. Certification thường mất 1-3 ngày.
- [ ] Bị từ chối: đọc báo cáo, sửa qua PR, quay lại bước 1 với version mới.
- [ ] Đạt: cài thử từ Microsoft Store trên một máy khác, không có cảnh báo cert.

## Debug đóng gói ở local

Chạy cả chuỗi publish → stamp → pack mà không cần push tag:

```powershell
./pack-msix.ps1 -Version 0.0.1
```

Kết quả: `PdfReaderApp_0.0.1.msix` ở gốc repo, ký bằng `devcert.pfx` local (tự sinh nếu chưa có; cài một lần bằng `winapp cert install .\devcert.pfx` với quyền Administrator; sau khi đổi Publisher trong manifest thì xoá file này để sinh lại). `*.msix`, `*.pfx` và `publish/` đã nằm trong `.gitignore`.
