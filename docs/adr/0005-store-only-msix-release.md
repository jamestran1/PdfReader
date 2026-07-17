---
status: accepted
---

# Phân phối Store-only: MSIX đóng gói bằng winapp CLI, version theo git tag

Trí Thư phát hành tới người dùng **chỉ qua Microsoft Store** (grilling #93). Store tự re-sign MSIX bằng cert Microsoft và tự đẩy update — dự án không bao giờ mua code-signing cert, không viết auto-updater. Không có kênh tải trực tiếp cho user; GitHub Release chỉ lưu artifact + notes phục vụ dev.

- **Đóng gói: winapp CLI** (không dùng Windows Application Packaging Project). Lý do: repo build thuần dotnet CLI (slnx, không phụ thuộc Visual Studio); winapp pack nhận thẳng output `dotnet publish` + `Package.appxmanifest`, chạy được cả local lẫn GitHub Actions.
- **Version: git tag `vX.Y.Z` là nguồn sự thật duy nhất.** Không file nào trong repo chứa version; CI đọc tag và stamp assembly version + manifest version (`X.Y.Z.0` — Store bắt số cuối = 0). Không bao giờ lệch version giữa csproj và manifest vì không có chỗ thứ hai.
- **Publish: self-contained, x64-only.** User Store không có .NET 10 runtime và MSIX không khai báo được .NET desktop runtime làm dependency → self-contained là bắt buộc. arm64 để lại (vec0.dll trong repo chỉ có bản x64) — issue tương lai.
- **Pipeline: CI build, upload tay.** Push tag → Actions: `dotnet test` (gạch chắn) → publish → winapp pack → MSIX artifact + GitHub Release (`--generate-notes`). Con người tải MSIX, **smoke test cài thật trên local** (bắt buộc — AppData ảo hóa/package identity là lớp lỗi test suite không bắt được), rồi upload Partner Center và viết tay "What's new" tiếng Việt. Không tự động submit qua Store API (cần Entra ID app registration — chưa đáng khi cadence còn thưa; dual-control tự nhiên cho hành động public).

## Considered options

- **GitHub Releases (zip/sideload MSIX) làm kênh chính hoặc kênh phụ:** zip không ký → SmartScreen chặn; sideload MSIX bắt buộc cert tin cậy (~$200+/năm) hoặc bắt user cài cert self-signed; loại — Store-only cho mọi bản tới user.
- **Windows Application Packaging Project (.wapproj):** ổn định, nhiều tài liệu, nhưng cần msbuild + UWP workload, VS-centric, thêm project vào solution; loại.
- **Full-auto submit qua Store API / msstore CLI:** đáng đầu tư khi release đều đặn; hoãn.
- **MinVer/Nerdbank.GitVersioning:** thêm dependency và cơ chế ngầm, vẫn phải map sang `X.Y.Z.0`; tag thủ công là đủ cho dự án solo.

## Consequences

- Bước 0 một lần: đăng ký individual account **free** tại storedeveloper.microsoft.com (xác minh ID + selfie), reserve tên "Trí Thư", đưa Package Identity vào `Package.appxmanifest`; tạo `PRIVACY.md` publish qua GitHub Pages (bắt buộc cho listing — app gửi nội dung tài liệu tới OpenAI bằng key user tự nhập); listing tiếng Việt, mọi thị trường; dev cert local (`winapp cert generate` + install) cho smoke test.
- Dữ liệu bản unpackaged hiện tại (`%AppData%\PdfReaderApp`) **không tự sang** bản MSIX (AppData ảo hóa) — user hiện hữu (tác giả) bắt đầu với library rỗng hoặc copy tay.
- Danh tính app, user base và kênh update gắn với Store; rời Store sau này = mất kênh update lẫn danh tính đã ký.
- Glossary: xem CONTEXT.md mục "Phát hành (Release)" (Release, Package Identity, Submission, MSIX smoke test).
