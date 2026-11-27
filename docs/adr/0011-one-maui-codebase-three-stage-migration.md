---
status: accepted
---

# Một codebase MAUI cho Windows và macOS, chuyển qua ba chặng

Trí Thư hôm nay là một app WPF trên `net10.0-windows`, chỉ chạy trên Windows. Mục tiêu là cùng một ứng dụng chạy trên Windows và macOS (Apple Silicon). Ta thay WPF bằng MAUI 11 trên cả hai nền và giữ một codebase: Lõi dùng chung là project `net11.0` thuần, hai nền chỉ khác nhau ở đầu MAUI. Việc chuyển đi qua ba chặng để `main` luôn phát hành được trong lúc làm và có một điểm so sánh cố định.

## Quyết định

- **MAUI 11 thay WPF trên cả hai nền.** Đầu MAUI chạy trên Windows (backend WinUI 3) và trên macOS (Mac Catalyst). WPF bị gỡ sau khi chuyển; không giữ hai codebase.
- **Lõi dùng chung là project `net11.0` thuần**, không tham chiếu WPF lẫn MAUI. Models, Core, Services và ViewModels nằm trong đó. Thứ chỉ có ở một nền đi vào lõi qua interface:
  - *Cập nhật UI thread:* lõi không dùng `Dispatcher` của WPF. Lõi nhận một interface cập nhật UI thread; đầu cung cấp hiện thực.
  - *Cửa sổ, dialog, clipboard:* đi qua interface của nền.
  - *Đường dẫn dữ liệu:* lõi nhận đường dẫn dữ liệu qua constructor, không tự gọi `Environment.GetFolderPath(ApplicationData)`, vì .NET 11 trên Mac Catalyst trả về `<Documents>/.config`, không phải chỗ app nên ghi.
  - *Key API:* tách interface `IApiKeyStore`. Bản Windows giữ DPAPI; bản macOS dùng Keychain qua `SecureStorage`.
  - *Mở link:* `Process.Start` với `UseShellExecute = true` thay bằng `Launcher` hoặc `Browser` của lớp nền.
- **Kiểu hình học trong Core là kiểu `double` tự viết**, giữ ngữ nghĩa WPF `Rect`. Không file Core nào dùng `Dispatcher`, hôm nay và sau khi tách.
- **Dữ liệu người dùng Windows và Package Identity giữ nguyên.** Trên Windows, đường dẫn dữ liệu truyền vào lõi vẫn là đường dẫn hiện tại (`%APPDATA%\PdfReaderApp`, tính bởi `AppDataDirectory.Resolve`); không đổi sang `FileSystem.AppDataDirectory` của MAUI, vì với app packaged API đó trỏ `ApplicationData.Current.LocalFolder`, khác chỗ dữ liệu hiện tại. `Package.appxmanifest` giữ nguyên `Name`, `Publisher` và `Application Id="App"`. Trên macOS, đường dẫn dữ liệu truyền vào lõi nằm trong container của App Sandbox.
- **Ba chặng:**
  - *Trước chặng 1:* cả solution lên .NET 11. Việc này tách khỏi MAUI vì không phụ thuộc research nào.
  - *Chặng 1, tách lõi:* Models, Core, Services, ViewModels chuyển sang project `net11.0` thuần bằng các PR nhỏ lên `main`; giao diện WPF vẫn là giao diện đang phát hành.
  - *Chặng 2, dựng song song:* đầu MAUI dựng cạnh bản WPF và chạy trên Windows để so với bản WPF.
  - *Chặng 3, chuyển:* gỡ WPF và đổi pipeline phát hành trong một PR.
- **Đóng băng UI sau chặng 1.** Từ khi hết chặng 1 tới khi gỡ WPF, giao diện WPF chỉ nhận sửa lỗi; tính năng mới đi vào Lõi dùng chung, màn hình mới chờ đầu MAUI.
- **Mức so sánh của tính năng là `main` tại điểm đóng băng UI.** Sau khi chuyển, cả hai nền phải qua Ma trận tính năng, test và checklist smoke; tính năng nào không mang lên được là một quyết định ghi rõ. Windows và macOS phát hành cùng một tag `vX.Y.Z`; Windows vẫn chỉ qua Microsoft Store.
- **Ràng buộc phát hành cho đầu MAUI.** `Package.appxmanifest` ở gốc repo vẫn là nguồn duy nhất của Package Identity; lệnh `dotnet publish` của Windows trỏ sang đầu MAUI với `-f net11.0-windows10.0.19041.0` và `-p:WindowsPackageType=None`. Project Mac Catalyst đặt `SupportedOSPlatformVersion` 18.2 (macOS 15.2) tường minh: bỏ trống thì nó rơi về phiên bản SDK, tức app chỉ chạy trên macOS 27.2 trở lên. Chỉ xuất arm64. Hai kênh phát hành macOS (tải trực tiếp và Mac App Store) build từ cùng một mã nguồn; chúng khác nhau ở certificate, provisioning profile và một cờ build chọn kênh, vì bản Mac App Store không được chứa cơ chế báo bản mới ngoài Store.
- **Phạm vi.** macOS chỉ có bản Apple Silicon (arm64). Giữ Material Design 3 trên cả hai nền, theo thói quen Mac ở chỗ người dùng bắt buộc mong đợi (menu bar, phím tắt Cmd, file dialog, cuộn). Không chia sẻ hay đồng bộ dữ liệu giữa Windows và macOS.

## Considered options

- **Lõi dùng chung với hai đầu: giữ WPF cho Windows, MAUI chỉ cho macOS:** phải viết và bảo trì hai lớp giao diện, vì `MaterialDesignThemes` không có bản MAUI nên đầu MAUI cần lớp Material 3 riêng; loại, chọn một đầu MAUI cho cả hai nền.
- **Avalonia thay MAUI:** đã chọn MAUI trước khi lập map; không so lại ở đây.
- **Nâng .NET 11 cùng lúc với việc đổi sang MAUI:** nâng .NET 11 và chặng 1 không phụ thuộc research nào nên làm ngay được; gộp lại thì cả hai phải chờ research của MAUI; loại.
- **Dựng MAUI rồi gỡ WPF ngay, không có giai đoạn chạy song song:** không còn bản WPF trên cùng máy để so sánh; loại, chọn ba chặng.
- **Lõi dùng chung tham chiếu thẳng gói MAUI** (`Microsoft.Maui.Graphics.Rect` thay `Rect` tự viết, `IDispatcher` thay interface riêng): các gói này có TFM `net11.0`, nhưng lõi sẽ mang dependency mang tên MAUI từ chặng 1, trong khi WPF vẫn là giao diện; loại cho lõi. Đầu MAUI vẫn có thể hiện thực interface của lõi bằng `IDispatcher` của MAUI.
- **Lõi dùng chung tự phân nhánh theo nền** (`#if`, `OperatingSystem.IsWindows()`): lõi vẫn phải tham chiếu kiểu của từng nền và không có seam để test thay bằng fake (CS-11); loại, chọn interface.
- **Đổi sang `FileSystem.AppDataDirectory` của MAUI:** với app packaged nó trỏ `ApplicationData.Current.LocalFolder.Path`, khác chỗ dữ liệu hiện tại; app mở thư viện rỗng dù dữ liệu vẫn còn; loại.
- **Dùng `SecureStorage` cho cả Windows:** định dạng khác với ba file `settings*.dat` hiện tại, nên key cũ không đọc được; loại.

## Consequences

- **Thu hẹp ADR "Markdown dùng Markdig và renderer WPF tự viết".**
  - Câu "Ta dùng Markdig để parse Markdown, rồi tự viết renderer WPF từ syntax tree thay vì dùng WebView2 hoặc HTML engine." và câu "renderer WPF tự quyết định bố cục theo Material Design của app." nay chỉ đúng cho bản WPF còn phát hành. Sau khi chuyển, renderer thuộc đầu MAUI; viết lại trên control MAUI hay render HTML trong WebView do ticket prototype "Renderer Markdown trên MAUI: citation, code fence, sáng và tối" (#320) trả lời, rồi ghi bằng ADR mới. Phần Markdig và các quy tắc về nội dung của ADR cũ giữ nguyên.
  - Câu "Chat AI, Chat history, Note và Bản soạn dùng chung `MarkdownPresentation` và `MarkdownPresenter`" còn đúng một nửa: `MarkdownPresentation` (Models) vào Lõi dùng chung; `MarkdownPresenter` và `MarkdownTextBlock` là control WPF, ở lại bản WPF; đầu MAUI có renderer riêng.
- **Thu hẹp ADR "Phân phối Store-only: MSIX đóng gói bằng winapp CLI, version theo git tag".**
  - "Trí Thư phát hành tới người dùng **chỉ qua Microsoft Store**" và "Không có kênh tải trực tiếp cho user" nay chỉ đúng cho Windows Release. macOS Release có thêm kênh tải trực tiếp (bản ký Developer ID, đã notarize) bên cạnh Mac App Store.
  - "dự án không bao giờ mua code-signing cert, không viết auto-updater" thu hẹp còn nửa sau: kênh tải trực tiếp trên macOS cần certificate của Apple Developer Program. App vẫn không tự cập nhật: chỉ báo có bản mới và mở trang tải.
  - "winapp pack nhận thẳng output `dotnet publish` + `Package.appxmanifest`" giữ nguyên về công cụ; đổi ở đầu vào, là output publish của đầu MAUI với `-p:WindowsPackageType=None`. Đi đường này là đề xuất của research "MSIX và Microsoft Store cho app MAUI trên Windows, giữ Package Identity" (#326); ticket prototype "Smoke test nâng cấp tại chỗ từ bản WPF sang bản MAUI trên Windows" (#336) kiểm chứng.
  - "User Store không có .NET 10 runtime" chỉ còn đúng sau khi đổi số thành .NET 11; luận điểm về .NET runtime không đổi. "x64-only" vẫn đúng cho Windows Release. "arm64 để lại (vec0.dll trong repo chỉ có bản x64)" chỉ còn đúng cho Windows Release, vì macOS Release chỉ có arm64.
  - "Push tag → Actions: `dotnet test` (gạch chắn) → publish → winapp pack → MSIX artifact + GitHub Release" được mở rộng: cùng tag đó build thêm macOS Release.
- **Không đổi ADR "Toàn bộ app cấp phép AGPL-3.0 (hệ quả của iText 8)".** Hướng xử lý khi lên Mac App Store chờ ticket "Hướng xử lý AGPL khi lên Mac App Store" (#332).
- **Hành vi code hiện có bị đổi:**
  - *Đường dẫn dữ liệu:* `AppDataDirectory.Current()` (Core) là chỗ gọi `Environment.GetFolderPath(ApplicationData)` và tạo thư mục. Nay nó được gọi ngay trong lõi: ở `DefaultServices.Create()`, ở các giá trị mặc định của `MainViewModel` và `CompositionsViewModel` (`?? DefaultServices.Create...(AppDataDirectory.Current())`) và ở `WindowsSettingsService` (`storageDirectory ?? Core.AppDataDirectory.Current()`). Các lời gọi này bỏ đi, và `Current()` rời Lõi dùng chung theo đầu; chỉ `AppDataDirectory.Resolve` (hàm thuần) ở lại, đầu dùng nó để tính đường dẫn rồi truyền vào constructor. Việc tạo thư mục do nơi dựng dịch vụ lo: `DefaultServices.Create(dataDir)` và `WindowsSettingsService` đã gọi `Directory.CreateDirectory`.
  - *Cập nhật UI thread:* `ChatViewModel` và `IndexingViewModel` gọi `System.Windows.Application.Current?.Dispatcher`; `NotificationViewModel` và `OpenSetPersistence` dùng `DispatcherTimer`; bốn class có seam `DispatcherProvider` (`Func<Dispatcher?>`): `OpenSetPersistence`, `ReadingSessionDependencies`, `ReadingSessionViewModel`, `MainViewModelSessionFactory`. Tất cả thay bằng interface cập nhật UI thread của lõi. `Dispatcher` chỉ còn ở lớp UI: `App` (`DispatcherUnhandledException`), `PdfViewerControl`, `CompositionView`, `ScrollSettleTimer`.
  - *Dialog, clipboard, theme:* `DesktopPrompts` (Core) gọi `MessageBox` và `Clipboard` qua `ConfirmDeleteComposition` và `CopyText`; nó rời Core, còn `CompositionsCallbacks.ConfirmDelete`, `CompositionsCallbacks.CopyToClipboard` và `ConfirmDeleteComposition` của `MainViewModelServices` nhận hiện thực của đầu. `MainViewModel` mở `Microsoft.Win32.OpenFileDialog` rồi gọi `ImportAndOpen(dialog.FileName)`: hộp thoại chọn file đi qua interface của nền. `MaterialDesignThemeService` (Services) dùng WPF và `MaterialDesignThemes`, nên `DefaultServices.CreateThemeService()` không còn dựng được nó từ lõi.
  - *Key API:* `WindowsSettingsService` gộp key API (DPAPI vào `settings.dat`, `settings.gemini.dat`, `settings.claude.dat`) với cài đặt model (`ai-models.json`) và theme (`theme.pref`) trong `ISettingsService`. Key API tách ra `IApiKeyStore`; hiện thực Windows giữ `ProtectedData` với `DataProtectionScope.CurrentUser` và entropy rỗng để đọc được key cũ.
  - *PDFium và `System.Drawing`:* `RenderEngine`, `PdfObjectManager` (Core) và `LibraryService` (Services) dùng `PdfiumViewer.Net.WPF`, gói chỉ có `net8.0-windows7.0`; `RenderEngine` còn dùng `System.Drawing`. Ba file này không thể sang project `net11.0` nguyên vẹn, nên chặng 1 không chỉ là di chuyển file; thay lớp PDFium ngay trong chặng 1 hay tách phần PDF ra project chỉ-Windows trước là câu hỏi của ticket "Thay PdfiumViewer.Net.WPF trong chặng 1 hay chờ prototype viewer" (#339). `SlotHitTesterTests`, `TextSelectionResolverTests` dùng `Rect`, `Point` của WPF và `PdfDocumentLoadCharacterizationTests` gọi `PdfiumViewer.Core`: đây là lưới an toàn của việc thay.
  - *Kiểu hình học:* `SelChar`, `SelectionResult`, `SlotHitTester`, `TextSelectionResolver`, `PdfObjectManager` dùng `Rect`, `Point` của WPF; chúng đổi sang kiểu `double` tự viết trong Core, cùng kiểu số với `PageSlot`.
  - *Công cụ dev:* `WindowSnapshot` (Services) dùng `Application`, `RenderTargetBitmap` và `DispatcherTimer` để chụp cửa sổ WPF cho `scripts/snapshot.ps1`; nó đi theo phần WPF, không thuộc Lõi dùng chung.
  - *Mở link:* `MarkdownTextBlock` (Controls) mở link bằng `Process.Start` với `UseShellExecute = true`. Research "App Sandbox đối chiếu với hành vi file và mạng của Trí Thư" (#327) kết luận trên Unix đường này không trao URL cho hệ thống, nên đầu MAUI dùng `Launcher` hoặc `Browser`.
  - *CI và đóng gói:* `check.yml`, `ai-fix.yml` và `release.yml` đều dùng `dotnet-version: 10.0.x`, nên phải đổi sang .NET 11 cùng lúc với việc solution lên .NET 11. `pack-msix.ps1` publish `src/PdfReaderApp/PdfReaderApp.csproj`; lệnh này đổi ở chặng 3, cùng với việc gỡ WPF.
  - *Test:* `tests/PdfReaderApp.Tests` cũng là `net10.0-windows` với `UseWPF`. Test của Lõi dùng chung phải chạy trên `net11.0` không WPF; cách tách test chạm kiểu WPF hoặc PDFium thuộc chiến lược test trên hai nền, chưa quyết.
- **Chưa quyết, không thuộc ADR này:** lớp PDFium thay `PdfiumViewer.Net.WPF` từ chặng nào (#339); renderer Markdown trên MAUI (#320); runtime Windows App SDK đi framework-dependent hay self-contained (#336); hướng xử lý AGPL và Mac App Store (#332); sign, notarize và hai kênh cùng bundle ID trên một Mac (#337); chiến lược test trên hai nền (chờ Ma trận tính năng, #317); lớp Material 3 trên MAUI (#331).
- Glossary: xem `GLOSSARY.md` các mục "Kiến trúc trên hai nền", "Chuyển sang MAUI" và "Phát hành (Release)".
