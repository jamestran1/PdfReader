# Runbook phát hành Trí Thư

Quy trình đưa một Release lên Microsoft Store (ADR 0005, ADR 0006). Thuật ngữ Release, Package Identity, Submission, MSIX smoke test: xem `CONTEXT.md` mục "Phát hành (Release)".

Thành phần trong repo:

| Thành phần | Vai trò |
|---|---|
| `.github/workflows/release.yml` | Chạy khi push tag `v*.*.*`: `dotnet test` (gạch chắn) → `pack-msix.ps1` → artifact `msix` + GitHub Release `--generate-notes`. |
| `pack-msix.ps1` | Publish self-contained x64 → stamp version `X.Y.Z.0` vào manifest + assembly → `winapp package`. Chạy y hệt ở local: `./pack-msix.ps1 -Version 0.0.1`. |
| `Package.appxmanifest`, `Assets/` | Manifest MSIX (version `0.0.0.0` là placeholder, version thật lấy từ tag) và logo/tile. |
| `LICENSE`, `THIRD-PARTY-NOTICES.md` | AGPL-3.0 (ADR 0006): điều kiện chặn mọi Submission. |

## A. One-time setup

Làm một lần trước Submission đầu tiên (chi tiết: #99). Bước nào chưa xong thì chưa được Submission.

- [ ] Đăng ký individual developer account (miễn phí) tại storedeveloper.microsoft.com, xác minh giấy tờ tùy thân + selfie, vào được Partner Center "Apps & Games".
- [ ] Reserve tên app "Trí Thư" trên Partner Center (nếu tên có dấu bị từ chối, chốt phương án dự phòng và cập nhật `DisplayName` trong `Package.appxmanifest`).
- [ ] Lấy Package Identity (Product identity: `Name`, `Publisher`, `PublisherDisplayName`) từ Partner Center, thay các giá trị `Placeholder` trong `Package.appxmanifest`, merge qua PR.
- [ ] Thay logo/tile placeholder trong `Assets/` (44x44, 150x150, 310x150, StoreLogo, SplashScreen) bằng bản thật, merge qua PR.
- [ ] Viết `PRIVACY.md` (nội dung tài liệu chỉ gửi tới OpenAI khi người dùng chủ động chat bằng API key của chính họ; app không có server, không thu thập dữ liệu) và publish qua GitHub Pages; mở URL ở trình duyệt chưa đăng nhập để kiểm tra.
- [ ] Nháp Store listing tiếng Việt, mở mọi thị trường: mô tả, screenshots (`pwsh scripts/snapshot.ps1 -Out x.png` cho ảnh cửa sổ chính), age rating questionnaire, privacy URL, khai báo license AGPL-3.0 + link mã nguồn.
- [ ] Cài [winapp CLI](https://github.com/microsoft/winappCli) trên máy smoke test (`winapp --help` chạy được).

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

- [ ] Tin cert đã ký package. CI sinh dev cert mới ở mỗi lần chạy nên bước này làm lại cho mỗi Release (PowerShell **Administrator**):

  ```powershell
  $sig = Get-AuthenticodeSignature .\PdfReaderApp_X.Y.Z.msix
  Export-Certificate -Cert $sig.SignerCertificate -FilePath .\release-signer.cer
  winapp cert install .\release-signer.cer
  ```

- [ ] Cài: `Add-AppxPackage .\PdfReaderApp_X.Y.Z.msix`, không báo lỗi.
- [ ] Mở "Trí Thư" từ Start menu: app chạy, không cần cài .NET runtime, chạy được khi chưa có API key (điều kiện certification).
- [ ] Mở một PDF vào Library, mở từ Library: trang render đúng, cuộn và zoom được.
- [ ] Tạo Workspace, thêm Document vào Workspace, mở vài Tab.
- [ ] Nhập OpenAI API key trong Settings, chat AI về tài liệu: có câu trả lời.
- [ ] Đóng app, mở lại: Library, Workspace, Tab và Chat thread còn nguyên. Dữ liệu phải nằm trong thư mục ảo hóa `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Roaming\PdfReaderApp`, không phải `%APPDATA%\PdfReaderApp`.
- [ ] Gỡ cài sạch: `Get-AppxPackage *Placeholder* | Remove-AppxPackage` (thay `Placeholder` bằng Package Name thật sau one-time setup), app biến mất khỏi Start menu.

### 4. Upload Partner Center

- [ ] Tạo Submission mới, upload `PdfReaderApp_X.Y.Z.msix` (package không cần ký lại: Store tự re-sign sau certification).
- [ ] Viết tay "What's new" tiếng Việt, 3-5 gạch đầu dòng hướng người dùng (bản mới làm được gì). **Không** dán commit log hay notes của GitHub Release.
- [ ] Listing còn khai báo AGPL-3.0 và link mã nguồn trỏ đúng tag: `https://github.com/jamestran1/PdfReader/tree/vX.Y.Z`.

### 5. Submit và theo dõi certification

- [ ] Submit. Certification thường mất 1-3 ngày.
- [ ] Bị từ chối: đọc báo cáo, sửa qua PR, quay lại bước 1 với version mới.
- [ ] Đạt: cài thử từ Microsoft Store trên một máy khác, không có cảnh báo cert.

## Debug đóng gói ở local

Chạy cả chuỗi publish → stamp → pack mà không cần push tag:

```powershell
./pack-msix.ps1 -Version 0.0.1
```

Kết quả: `PdfReaderApp_0.0.1.msix` ở gốc repo, ký bằng `devcert.pfx` local (tự sinh nếu chưa có; cài một lần bằng `winapp cert install .\devcert.pfx` với quyền Administrator). `*.msix`, `*.pfx` và `publish/` đã nằm trong `.gitignore`.
