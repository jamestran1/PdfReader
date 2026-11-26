# App Sandbox đối chiếu với hành vi file và mạng của Trí Thư (research cho #327)

Ngày: 2026-11-26. Ticket: #327, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Trí Thư sắp chạy trên Mac Catalyst trong một bản đã bật App Sandbox, dùng chung cho kênh tải trực tiếp và Mac App Store. Ticket hỏi: sandbox chặn gì trong hành vi file, mạng, clipboard và tiến trình của app hiện tại; thư mục dữ liệu nằm ở đâu và file người dùng chọn được đọc ra sao; key API (nay mã hoá bằng DPAPI) lưu trên macOS bằng cách nào trong khi bản Windows vẫn đọc được key cũ; và hai bản cùng bundle ID nhưng sign khác nhau có dùng chung dữ liệu không. Tôi rà mã nguồn nhánh `main` (chỉ đọc) rồi đối chiếu với tài liệu sống của Apple và Microsoft và với mã nguồn .NET 11, MAUI 11, SDK Mac Catalyst .NET 11 tính đến ngày 26/11/2026.

Cách đọc: **[Fact]** là điều tài liệu hoặc mã nguồn viết, có link ngay tại chỗ. **[Suy luận]** là nhận định của tôi. "Chưa kiểm chứng" là chưa tìm được nguồn chính hoặc chưa chạy thử trên Mac (tôi không có máy Mac trong phiên này). Ghi chú về nguồn: địa chỉ archive cũ của App Sandbox Design Guide nay chuyển hướng về trang DocC mới `documentation/security/app-sandbox`; tôi trích các trang sống này, không trích lại nội dung archive từ trí nhớ.

## Tóm tắt

- App chỉ có bốn hành vi sandbox cần để ý: ghi thư mục dữ liệu (cho phép mặc định), HTTP đi tới AI Provider (cần `com.apple.security.network.client`), đọc PDF người dùng chọn (cần `com.apple.security.files.user-selected.read-only`), ghi clipboard (không cần entitlement). Không có xuất file, file tạm hay tiến trình con. [Fact] mục 1 và 2
- Không có hành vi nào rơi vào danh sách Apple cấm trong sandbox (Apple Events, preference của app khác, cài đặt mạng, kết thúc app khác). [Fact] [a-protect]
- `Entitlements.plist` cho cả hai kênh gồm ba khoá: `app-sandbox`, `network.client`, `files.user-selected.read-only`. Template MAUI 11 đã có hai khoá đầu. SDK chỉ tự thêm `network.client` ở build Debug, nên bản Release phải tự khai. [Fact] [maui-template-ent], [ios-compile-ent]; [Suy luận] cho danh sách ba khoá
- Thư mục dữ liệu nằm trong container `~/Library/Containers/<bundle ID>/`. Nếu giữ nguyên code, .NET 11 trên Mac Catalyst trả `ApplicationData` là `<Documents>/.config`, tức app ghi vào `Documents/.config/PdfReaderApp` trong container: sandbox cho phép, nhưng không đúng chỗ (quy ước là thư mục `Library`). Nên để lớp nền tảng truyền đường dẫn vào lõi. [Fact] [src-env-ios]; [Suy luận] về đường dẫn tuyệt đối
- `FilePicker` của MAUI có gọi `startAccessingSecurityScopedResource` nhưng dừng quyền ngay trong constructor; chỉ `OpenReadAsync()` mới cấp quyền lại. Trí Thư copy PDF vào container ngay khi import nên không cần security-scoped bookmark, nhưng phải đọc bản gốc qua `OpenReadAsync()`, không đọc `FullPath` bằng API `File.*`. [Fact] [maui-fs-ios], [a-access]; [Suy luận] về cách đổi seam
- Key API: `ProtectedData` chỉ chạy trên Windows. `SecureStorage` của MAUI có hỗ trợ Mac Catalyst và lưu vào Keychain; Apple ghi rõ app Mac Catalyst chỉ dùng được data protection keychain, kho này phân quyền theo keychain access groups. Bản Windows phải giữ nguyên code DPAPI hiện tại, vì `SecureStorage` trên Windows dùng định dạng khác. [Fact] [ms-protecteddata], [a-tn3137], [maui-securestorage-win]
- Hai bản cùng bundle ID cùng trỏ về một container. Từ macOS 14 hệ thống gắn container theo chữ ký mã, và với designated requirement mặc định của `codesign`, bản Developer ID và bản Mac App Store không tương thích: bản thứ hai bị hỏi quyền khi vào container, từ chối lúc khởi chạy thì app không mở được. Task `Codesign` của SDK không tự đặt DR. [Fact] [a-access], [a-tn3127], [ios-codesign]; [Suy luận] về hệ quả cho Trí Thư
- Cài cả hai bản trên một Mac: `.pkg` của SDK cài vào `/Applications`, cùng vị trí với App Store, nên bản sau thay bản trước. [Suy luận]

## 1. Danh sách hành vi chạm file, mạng, clipboard, tiến trình

Nguồn của cột "File trong code" là mã nguồn `src/PdfReaderApp` (và `scripts/`, `native/`) trên nhánh `main`.

| Hành vi | File trong code | Chi tiết |
|---|---|---|
| Thư mục dữ liệu | `Core/AppDataDirectory.cs` | `Environment.GetFolderPath(SpecialFolder.ApplicationData)` cộng `PdfReaderApp`; biến `TRITHU_DATA_DIR` ghi đè; `Directory.CreateDirectory`. Dùng qua `ViewModels/DefaultServices.cs`, `Services/WindowsSettingsService.cs`, `App.xaml.cs` |
| Sáu SQLite | `Services/Sqlite*Store.cs`, `Services/SqliteDocumentIndex.cs`, `ViewModels/DefaultServices.cs` | `index.db` (bật WAL, nạp extension `vec0.dll` từ `AppContext.BaseDirectory`), `library.db`, `chats.db`, `notes.db`, `workspaces.db`, `compositions.db`; mỗi cái một `Data Source=` (ADR 0002, `docs/adr/0002-per-feature-sqlite-databases.md`) |
| Copy PDF vào Library | `Services/LibraryService.cs` | `DocumentId.FromFile` (mở stream, SHA-256), rồi `File.Copy` vào `<data>/library/<id>.pdf`; `Remove` xoá file |
| Thumbnail | `Services/LibraryService.cs`, `Converters/LibraryCoverBrushConverter.cs`, `Converters/HasThumbnailToVisibilityConverter.cs` | ghi `<data>/library/thumbs/<id>.png`, đọc lại bằng `BitmapImage` |
| Mở Document để đọc | `Controls/Viewer/PdfDocumentSession.cs`, `Services/PdfFilePageTextSource.cs`, `Services/PdfMetadataReader.cs` | đọc bản sao trong Library bằng PDFium và iText |
| Cài đặt thường | `Services/WindowsSettingsService.cs` | `ai-models.json`, `theme.pref` dạng text |
| Key API | `Services/WindowsSettingsService.cs` | DPAPI (`ProtectedData`, `CurrentUser`, entropy rỗng) vào `settings.dat` (OpenAI), `settings.gemini.dat`, `settings.claude.dat` |
| Chọn file để mở | `ViewModels/MainViewModel.cs` (dòng 390) | `Microsoft.Win32.OpenFileDialog` rồi `ImportAndOpen(dialog.FileName)` |
| HTTP tới AI Provider | `Services/AiChatClientFactory.cs`, `Services/AiEmbeddingGeneratorFactory.cs`, `Services/GeminiClientFactory.cs` | SDK `OpenAI`, `Anthropic`, `Google.GenAI`; app không tự tạo `HttpClient` |
| Mở link ngoài | `Controls/MarkdownTextBlock.cs` (dòng 74) | `Process.Start` với `UseShellExecute = true`, chỉ scheme `http` và `https` |
| Clipboard | `Core/DesktopPrompts.cs` (dòng 20) | `Clipboard.SetDataObject(text, copy: true)`; dùng ở `CompositionsViewModel` và `MainViewModel` |
| Crash log | `App.xaml.cs` (dòng 54), `MainWindow.xaml.cs` (dòng 22) | `File.WriteAllText("pdf_crash_log.txt", ...)`: đường dẫn tương đối, phụ thuộc thư mục làm việc |
| Biến môi trường | `Core/AppDataDirectory.cs`, `Core/SnapshotRequest.cs` | `TRITHU_DATA_DIR`; `TRITHU_SCREENSHOT`, `_DELAY`, `_OPEN`, `_PAGE`, `_ZOOM`, `_PANEL`, `_DIALOG` |
| Công cụ chụp ảnh dev | `scripts/snapshot.ps1`, `Services/WindowSnapshot.cs` | script chạy file `.exe` Windows với `TRITHU_DATA_DIR` ở thư mục tạm, `-Open` truyền một PDF, xuất PNG ra đường dẫn tuỳ ý |
| Thư viện native | `native/vec0.dll`, `Services/SqliteDocumentIndex.cs` (dòng 33) | nạp qua `LoadExtension`; PDFium và SkiaSharp do thư viện nạp |

Ba điểm rà được bằng grep, đáng ghi lại vì ticket có hỏi tới:

- App không đọc key API từ biến môi trường hay user-secrets. Key chỉ vào qua màn Cài đặt rồi được mã hoá bằng DPAPI; `CODING_STANDARDS.md` quy tắc REPO-1 mới nhắc tới biến môi trường và user-secrets. [Fact]
- App không có xuất PDF hay ảnh, không có `SaveFileDialog`, không in ấn, không kéo thả file, không khởi chạy tiến trình con (ngoài dòng mở link). [Fact]
- App không dùng file tạm trực tiếp. Chỉ `scripts/snapshot.ps1` tạo một thư mục tạm làm thư mục làm việc cho app. [Fact]

## 2. Sandbox chặn gì, cần entitlement nào

| Hành vi | Sandbox | Entitlement | Cách xử lý |
|---|---|---|---|
| Ghi thư mục dữ liệu: 6 SQLite, PDF, thumbnail, cài đặt | cho phép | không | thư mục phải nằm trong container; trong container app có quyền đọc ghi đầy đủ [a-access] |
| Nạp `vec0` và thư viện native trong bundle | cho phép [Suy luận: đọc bundle của chính app] | không (về phía sandbox) | đổi thành `.dylib` nằm trong bundle và ký cùng identity: phần đó thuộc research pipeline phát hành |
| HTTP đi tới AI Provider | bị chặn mặc định | `com.apple.security.network.client` | khai trong `Entitlements.plist`; chỉ cần client vì app không lắng nghe kết nối đến [a-ent-net] |
| Đọc PDF người dùng chọn | bị chặn mặc định | `com.apple.security.files.user-selected.read-only` | `FilePicker.PickAsync`, đọc qua `OpenReadAsync()`, copy vào container (mục 3) [a-ent-usel-ro] |
| Picker mở được Downloads, Movies, Music, Pictures | cần thêm entitlement riêng | `com.apple.security.files.downloads.read-only`, `com.apple.security.assets.*.read-only` | thêm khi cần; Apple xếp các thư mục này là "sensitive folders" [a-xcode-sandbox] |
| Ghi clipboard | không nằm trong danh sách hạn chế | không | thay bằng `Clipboard.SetTextAsync`; cần kiểm trên Mac [a-protect], [maui-clipboard-ios] |
| Mở link `http` và `https` | không nằm trong danh sách hạn chế | không | thay `Process.Start` bằng `Launcher.OpenAsync` hoặc `Browser.OpenAsync`; cần kiểm trên Mac [ms-launcher] |
| `TRITHU_DATA_DIR` trỏ ra ngoài container | bị chặn | `com.apple.security.temporary-exception.*` (tài liệu sống không còn liệt kê) | chỉ dùng cho dev, trỏ vào trong container, hoặc dùng build Debug không bật sandbox [a-protect], [a-ent-index] |
| `TRITHU_SCREENSHOT*`: mở PDF và ghi PNG ở đường dẫn tuỳ ý | bị chặn nếu ngoài container | như trên | viết lại cho macOS với đường dẫn trong container [Suy luận] |
| Crash log đường dẫn tương đối | phụ thuộc thư mục làm việc | không | ghi vào thư mục dữ liệu [Suy luận] |
| Đọc biến môi trường | cho phép (không phải I/O file) | không | không đổi [Suy luận] |
| Apple Events, preference của app khác, cài đặt mạng, kết thúc app khác | bị cấm | ngoài phạm vi | app không dùng [a-protect] |

`Entitlements.plist` đề xuất cho cả hai kênh **[Suy luận]** từ bảng trên:

```xml
<key>com.apple.security.app-sandbox</key>
<true/>
<key>com.apple.security.network.client</key>
<true/>
<key>com.apple.security.files.user-selected.read-only</key>
<true/>
```

- Template `maui-mobile` của MAUI 11 ghi sẵn `app-sandbox` và `network.client`, kèm chú thích "this value is required to open outgoing network connections". [Fact] [maui-template-ent]
- `CompileEntitlements` của SDK chỉ tự thêm `network.client` "if we're building for Debug, and the sandbox is enabled" vì debugger cần nó. [Fact] [ios-compile-ent]
- Bảng entitlement của SDK xếp `app-sandbox`, `network.client`, `files.user-selected.*`, `files.downloads.*` ở nhóm `desktop` với `requiresProvisioningProfile: false`. [Fact] [ios-compile-ent]
- Apple: "From Mac Catalyst, use `UIDocumentPickerViewController`" để chọn file ngoài sandbox, sau khi bật "Read Only" hoặc "Read/Write" ở mục "User Selected File". [Fact] [a-access], [a-xcode-sandbox]
- Microsoft viết rằng với app Mac Catalyst đã sandbox "the file picker won't open" nếu thiếu entitlement, và liệt kê sáu khoá (bốn khoá `read-only` cho movies, music, pictures, downloads; `user-selected.read-only`; `personal-information.photos-library`). Apple chỉ nêu `user-selected`. Cấu hình tối thiểu nào đủ trên máy thật: chưa kiểm chứng. [Fact] về hai nguồn, [ms-filepicker]
- Mở link: trên Unix, `Process.Start` với `UseShellExecute = true` vẫn đi vào `StartCore` như thường; nhánh `if (!startInfo.UseShellExecute)` chỉ bỏ qua bước chuẩn bị stream chuyển hướng. [Fact] [src-process]. Vậy URL không được trao cho hệ thống qua đường này [Suy luận]. Tài liệu của Microsoft ghi "shell" ở đây là "a graphical shell (similar to the Windows shell)" và không nói gì về macOS. [Fact] [ms-useshell]. Trên Mac Catalyst, `Browser.OpenAsync` với `BrowserLaunchMode.External` gọi `Launcher.Default.OpenAsync(uri)`, còn `Launcher` gọi `UIApplication.OpenUrlAsync`, tức trao URL cho hệ thống. [Fact] [maui-browser-ios], [maui-launcher-ios]. Microsoft: "To open the browser to a website, use the Browser API instead". [Fact] [ms-launcher]. Riêng `Launcher.OpenAsync(OpenFileRequest)` (mở file bằng app khác) ném `FeatureNotSupportedException` trên Mac Catalyst; Trí Thư không dùng tới. [Fact] [maui-launcher-ios]
- Apple có cả biến thể `com.apple.security.files.user-selected.read-write`; Trí Thư chỉ đọc bản gốc nên `read-only` đủ. [Fact] [a-ent-usel-rw], [a-ent-usel-ro]
- Microsoft lưu ý khi tạo provisioning profile: "you should ensure that any entitlements used by your app are also specified as capabilities in its provisioning profile". Cần làm cho cả profile Developer ID và profile Mac App Store Connect. [Fact] [ms-mac-ent]
- `com.apple.security.cs.allow-jit` do SDK thêm ở build Release (đã chốt trong `maccatalyst-release-pipeline.md` mục 1); tôi không kiểm lại trong ticket này.
- Các khoá `com.apple.security.temporary-exception.*`: tên của chúng không xuất hiện trong chỉ mục "Security entitlements" của Apple (trang này liệt kê các khoá sandbox còn lại như `user-selected`, `downloads`, `assets`), và không có trong bảng entitlement của SDK. [Fact] [a-ent-index], [ios-compile-ent]. Bốn địa chỉ tôi thử theo mẫu tên khoá đều trả 404, nhưng đây chỉ là bằng chứng phụ vì tôi có thể đoán sai địa chỉ. Chúng có còn được hệ thống nhận không: chưa kiểm chứng. Trí Thư không cần tới.
- Clipboard: tài liệu sống của Apple không có câu nào nói thẳng "pasteboard dùng được trong sandbox". Tôi dựa vào hai điều: `NSPasteboard` "transfers data to and from the pasteboard server" dùng chung, và pasteboard không có trong danh sách hành vi bị cấm lẫn danh sách entitlement. Kết luận "ghi clipboard được phép" vì thế là [Suy luận]. [a-nspasteboard], [a-protect]
- Cách chẩn đoán trên Mac: lọc Console bằng `type:error`, `subsystem:com.apple.sandbox.reporting`, `category:violation`; kiểm entitlement bằng `codesign -dvvv --entitlements - <app>`. [Fact] [a-diagnose], [a-protect]

Nguồn: [Apple: Protecting user data with App Sandbox][a-protect], [Apple: Accessing files from the macOS App Sandbox][a-access], [Apple: Configuring the macOS App Sandbox][a-xcode-sandbox], [Apple: network.client][a-ent-net], [Apple: files.user-selected.read-only][a-ent-usel-ro], [Apple: Security entitlements][a-ent-index], [Microsoft: File picker][ms-filepicker], [Microsoft: Launcher][ms-launcher], [SDK: CompileEntitlements.cs][ios-compile-ent]

## 3. Thư mục dữ liệu và file người dùng chọn

**Container.** "macOS creates a sandbox container on the file system (in `~/Library/Containers`) and associates it with your app. Your app has full read and write access to its sandbox container". [Fact] [a-access]. Trang "Migrating" ghi vị trí là `~/Library/Containers/<your app's bundle ID>`. [Fact] [a-migrate]. Thư mục con `Data` bên trong container: tài liệu Apple tôi đọc không nêu tên; chưa kiểm chứng. `NSHomeDirectory()`: "In macOS, it's the application's sandbox directory, or the current user's home directory if the application isn't in a sandbox". [Fact] [a-nshome]

**`Environment.GetFolderPath(ApplicationData)` của .NET 11 trên Mac Catalyst.** [Fact] từ mã nguồn:

- `IsiOSLike` đúng khi `TargetsMacCatalyst`; `Environment.iOS.cs` được biên dịch khi `IsiOSLike`, còn `Environment.GetFolderPathCore.Unix.cs` bị loại. [src-corelib-projitems]
- Trong `Environment.iOS.cs`, `ApplicationData` trả `CombineDocumentDirectory(".config")`, tức `NSSearchPath(NSDocumentDirectory)` cộng `.config`. [src-env-ios]
- `SearchPath` gọi `[NSFileManager URLsForDirectory:inDomains:]` với `NSUserDomainMask`, lấy phần tử cuối. [src-pal-searchpath]
- So sánh: trên macOS thuần (`TARGET_OSX`) cùng API trả `NSApplicationSupportDirectory` (thư mục Application Support của người dùng). [src-env-unix]

**[Suy luận]** Từ hai nguồn trên: giữ nguyên code thì app ghi vào `~/Library/Containers/<bundle ID>/Data/Documents/.config/PdfReaderApp`. Sandbox cho phép vì nằm trong container, Apple bảo các thư mục chuẩn của app đã sandbox "returns a location within the app's container". [a-protect]. Nhưng đó không phải vị trí quy ước: Microsoft ghi `FileSystem.AppDataDirectory` trên iOS và Mac Catalyst trả về thư mục `Library`, và Essentials tính nó từ `NSSearchPathDirectory.LibraryDirectory`. [ms-fshelpers], [maui-fs-apple]. Giá trị chính xác trên máy thật: chưa kiểm chứng.

**Đề xuất [Suy luận]:** giữ seam `AppDataDirectory.Resolve(getVariable, applicationDataFolder)` và để lớp nền tảng truyền đường dẫn. Windows truyền `%APPDATA%\PdfReaderApp` như hiện nay để giữ dữ liệu người dùng; Mac Catalyst truyền `FileSystem.Current.AppDataDirectory` cộng `PdfReaderApp`. Lõi thuần không cần biết nền tảng. Hai lưu ý kèm theo:

- `library.db` lưu `stored_path` và `thumb_path` tuyệt đối. Chấp nhận được vì mỗi nền tảng một container, nhưng dữ liệu không đi theo được khi đổi bundle ID. Microsoft dặn "Never hard-code or persist absolute sandbox paths" (câu viết cho iOS). [ms-fshelpers]. Cân nhắc lưu đường dẫn tương đối với thư mục dữ liệu.
- `Path.GetTempPath()` trên Mac Catalyst lấy `NSTemporaryDirectory()`. [src-path-ios], [src-pal-searchpath]. App không dùng file tạm; SQLite và iText có thể dùng [Suy luận].

**File người dùng chọn.**

- Apple: với URL nhận từ open panel, "The operating system starts security-scoped access on URLs passed from open panels, save panels, or items dragged to your app's icon in the Dock, as if you called `startAccessingSecurityScopedResource()`". [Fact] [a-access]
- Apple về `UIDocumentPickerViewController`: URL nhận được là security-scoped, phải gọi `startAccessingSecurityScopedResource()`, luôn dùng `NSFileCoordinator`, và "Don't save URLs that the open and move operations provide". [Fact] [a-docpicker]
- Mã nguồn MAUI 11: `FilePicker` trên Mac Catalyst tạo `UIDocumentPickerViewController` kiểu Open với `asCopy: false`, rồi gói mỗi URL bằng `NSFileCoordinator` vào `BookmarkDataFileResult`. [Fact] [maui-filepicker-ios]
- Constructor của `BookmarkDataFileResult` gọi `StartAccessingSecurityScopedResource`, tạo bookmark, rồi gọi `StopAccessingSecurityScopedResource` ngay trong `finally`. Chỉ `OpenReadAsync()` mới cấp quyền lại và trả `SecurityScopedStream` thu hồi quyền khi dispose. [Fact] [maui-fs-ios]. Apple yêu cầu mỗi lần gọi `startAccessingSecurityScopedResource()` phải có một lần `stopAccessingSecurityScopedResource()` tương ứng, nếu không app "leaks kernel resources"; mã nguồn MAUI cân bằng đúng hai lệnh này. [Fact] [a-startaccess]
- Microsoft: "`FullPath` doesn't always return the physical path to the file. To get the file, use the `OpenReadAsync` method". [Fact] [ms-filepicker]

**Có cần security-scoped bookmark không? Không.** Apple: "You only need to create security-scoped bookmarks when your app might try to access the bookmarked resource after it exits and relaunches". [Fact] [a-access]. `LibraryService.Import` chỉ chạm bản gốc trong lúc import (một lượt băm, một lượt copy), sau đó mọi lần đọc đều trỏ vào bản sao trong container. [Fact]

**[Suy luận] về thay đổi cần làm:** app hiện đọc bản gốc qua đường dẫn: `DocumentId.FromFile(sourcePath)`, `File.Copy(sourcePath, ...)`, `Path.GetFileName(sourcePath)`. Sau picker, quyền truy cập đã bị MAUI thu hồi, nên đọc `FullPath` bằng API `File.*` có thể thất bại (chưa kiểm chứng trên Mac). Nên đổi `Import` nhận một `Stream` đã mở cộng tên file: băm SHA-256 và copy từ cùng stream đó. Windows vẫn truyền `File.OpenRead(path)`.

Nguồn: [Apple: Accessing files from the macOS App Sandbox][a-access], [Apple: Migrating your app's files to its App Sandbox container][a-migrate], [Apple: NSHomeDirectory()][a-nshome], [Apple: UIDocumentPickerViewController][a-docpicker], [dotnet/runtime: Environment.iOS.cs][src-env-ios], [dotnet/runtime: System.Private.CoreLib.Shared.projitems][src-corelib-projitems], [dotnet/runtime: pal_searchpath.m][src-pal-searchpath], [dotnet/maui: FileSystem.ios.cs][maui-fs-ios], [dotnet/maui: FilePicker.ios.cs][maui-filepicker-ios], [Microsoft: File system helpers][ms-fshelpers]

## 4. Lưu key API trên macOS

**DPAPI không chạy trên macOS.** Microsoft: "`ProtectedData` class is supported on the Windows platform only. Its use on .NET Core on platforms other than Windows throws a `PlatformNotSupportedException`". Mã nguồn .NET 11: `Protect` gọi `CheckPlatformSupport()` trước, hàm này ném `PlatformNotSupportedException` khi `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` sai. [Fact] [ms-protecteddata], [src-protecteddata]

**`SecureStorage` của MAUI có hỗ trợ Mac Catalyst.**

- File `SecureStorage.ios.tvos.watchos.macos.cs` khớp mẫu `**\*.ios.*.cs`, và nhóm này được biên dịch cho `ios` và `maccatalyst`. [Fact] [maui-essentials-csproj]
- Bản Apple dùng `SecKeyChain.Add`, `QueryAsRecord`, `Remove` với bản ghi `SecKind.GenericPassword`: `Account = key`, `Service = Alias`, `Accessible = AfterFirstUnlock`. [Fact] [maui-securestorage-apple]
- `Service` bằng `<bundle id>.microsoft.maui.essentials.preferences`. [Fact] [maui-securestorage-shared], [maui-preferences-shared], [ms-securestorage]

**Lưu ở đâu.** Apple TN3137: macOS có hai kho keychain (dựa trên file và data protection). Với "Mac Catalyst apps" và "iOS Apps on Mac", data protection keychain "It's the default and only option". "Each user gets exactly one data protection keychain". Kho này "uses keychain access groups", danh sách nhóm được dựng từ entitlement có trong provisioning profile, và Keychain Access hiển thị nó thành "iCloud Keychain" hoặc "Local Items". [Fact] [a-tn3137]. Vậy [Suy luận] key không nằm trong container dưới dạng file do app quản lý, vì mỗi người dùng có đúng một kho keychain kiểu này. Trang `kSecUseDataProtectionKeychain` bổ sung: dùng khoá này "when accessing a macOS keychain item that behaves like an iOS keychain item", và Apple ghi "It's highly recommended that you set the value of this key to `true` for all keychain operations" để code dễ chuyển nền tảng. [Fact] [a-ksec-dp]

**Giới hạn cần biết.**

- API bất đồng bộ (`GetAsync`, `SetAsync`); `Remove` và `RemoveAll` đồng bộ. `RemoveAll` xoá mọi item có cùng `Service`. [Fact] [maui-securestorage-shared]
- Microsoft: "Performance may be impacted if you store large amounts of text"; gỡ app iOS không xoá item Keychain; "Keychain values written by `SecureStorage` may silently sync to the user's other Apple devices" nếu bật iCloud Keychain, và "This behavior is not controllable from .NET MAUI". [Fact] [ms-securestorage]. Mã nguồn không đặt `kSecAttrSynchronizable` tường minh; đồng bộ có xảy ra trên Mac Catalyst không: chưa kiểm chứng.
- Nhóm truy cập mặc định của app là `<Team ID>.<bundle ID>` (Apple gọi là application identifier, do Xcode thêm vào khi ký): "an app is always part of at least one group that contains only itself". [Fact] [a-keychain-sharing]. Không cần entitlement `keychain-access-groups` cho key riêng của app [Suy luận].

**Trừu tượng hoá để bản Windows vẫn đọc được key cũ [Suy luận].** `ISettingsService` hiện gộp ba việc: key API, model settings, theme. Tách key API ra một interface nhỏ đặt trong lõi thuần:

```csharp
public interface IApiKeyStore
{
    string? Get(AiProvider provider);
    void Save(AiProvider provider, string apiKey); // chuỗi rỗng: xoá key
    bool Has(AiProvider provider);
}
```

| Bản | Cách làm | Vì sao |
|---|---|---|
| Windows | `DpapiApiKeyStore`: giữ nguyên từng bước của `WindowsSettingsService` (tên file `settings.dat`, `settings.gemini.dat`, `settings.claude.dat`; `ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)`) | key cũ đọc được ngay, người dùng không phải nhập lại |
| Mac Catalyst, lựa chọn 1 | `SecureStorage.Default.GetAsync` và `SetAsync` | ít code nhất, Microsoft bảo trì |
| Mac Catalyst, lựa chọn 2 | gọi thẳng `SecKeyChain.QueryAsRecord` và `Add` (hoặc P/Invoke `SecItemAdd`, `SecItemCopyMatching`, `SecItemDelete`) theo mẫu lớp `KeyChain` của MAUI | đồng bộ như interface; tự đặt tên `Service` |

Tôi khuyến nghị lựa chọn 2: tám chỗ gọi trực tiếp `GetApiKey` hiện đều đồng bộ, và lựa chọn này không phụ thuộc `Service` cố định của Essentials. Nếu chọn lựa chọn 1 thì nên đổi interface sang `ValueTask<string?>` thay vì chặn chuỗi.

- Không dùng `SecureStorage` cho bản Windows. Mã nguồn MAUI 11 trên Windows mã hoá bằng `DataProtectionProvider("LOCAL=user")` rồi lưu vào `ApplicationData.Current.LocalSettings` (app đóng gói) hoặc file `securestorage.dat` (app không đóng gói). Định dạng này khác file `settings*.dat` của Trí Thư. [Fact] [maui-securestorage-win], [ms-securestorage]
- ADR 0005 (`docs/adr/0005-store-only-msix-release.md`) đã ghi dữ liệu `%AppData%` không tự chuyển sang bản MSIX. [Fact]. File key cũng nằm trong `%AppData%\PdfReaderApp`, nên cần tính đến việc người dùng phải nhập lại key khi Windows chuyển sang đóng gói [Suy luận].

Nguồn: [Microsoft: ProtectedData][ms-protecteddata], [Microsoft: Secure storage][ms-securestorage], [Apple TN3137: On Mac keychain APIs and implementations][a-tn3137], [Apple: Sharing access to keychain items among a collection of apps][a-keychain-sharing], [dotnet/runtime: ProtectedData.cs][src-protecteddata], [dotnet/maui: SecureStorage (Apple)][maui-securestorage-apple], [dotnet/maui: SecureStorage (Windows)][maui-securestorage-win]

## 5. Hai bản cùng bundle ID, sign khác nhau

**Container: một.** Vị trí container xác định theo bundle ID (`~/Library/Containers/<bundle ID>`), nên hai bản cùng bundle ID trỏ về cùng một thư mục. [Fact] [a-migrate]; [Suy luận] cho câu "cùng một thư mục"

**Quyền vào container lại phụ thuộc chữ ký.** Apple: "In macOS 14 and later, the operating system uses your app's code signature to associate it with its sandbox container. If your app tries to access the sandbox container owned by another app, the system asks the person using your app whether to grant access. If the person denies access while your app is launching and trying to enter the other app's sandbox container, your app fails to launch." Apple dẫn ví dụ: "two different versions of your app can have different designated requirements, leading the operating system to request permission when the second version tries to access the sandbox container", gồm trường hợp dùng đồng thời bản Mac App Store và bản build cục bộ. [Fact] [a-access]

**DR của hai kênh có tương thích không thì tuỳ cách ký.** Apple TN3127: hai app có DR tương thích khi mỗi app thoả DR của app kia; "If these apps have mutually compatible DRs then they share access to privacy-protected resources". DR do Xcode tạo cho Developer ID và cho Mac App Store tương thích với nhau. Nhưng DR mặc định của `codesign` thì khác: "These default DRs have one important limitation: Mac App Store and Developer ID variants of your app aren't mutually compatible." [Fact] [a-tn3127]

**SDK .NET dùng DR mặc định.** Task `Codesign` của SDK truyền `--sign`, `--timestamp`, `--keychain`, `--resource-rules`, `--entitlements`, rồi `CodesignExtraArgs`; không có tham số nào đặt designated requirement. `CodesignExtraArgs` là "Extra arguments passed to the 'codesign' tool when signing the app bundle". [Fact] [ios-codesign], [ios-build-props]

**[Suy luận] Hệ quả cho Trí Thư:** dùng lệnh publish mặc định thì bản tải trực tiếp (Developer ID) và bản Mac App Store có DR không tương thích. Người dùng từng chạy kênh này rồi chuyển sang kênh kia sẽ thấy hộp hội xin quyền truy cập container; từ chối lúc mở app thì app không khởi chạy được. Hai bản vẫn ghi vào cùng sáu file SQLite và cùng thư mục `library`, nên khi được cấp quyền thì dữ liệu dùng chung.

**Cách xử lý [Suy luận], chưa kiểm chứng trên Mac.** Ký bản Developer ID bằng DR kiểu Xcode mà TN3127 mô tả, truyền qua `CodesignExtraArgs`. DR này nhận cả chữ ký Mac App Store lẫn Developer ID của cùng Team ID:

```
anchor apple generic
and identifier "<bundle ID>"
and ( certificate leaf[field.1.2.840.113635.100.6.1.9] /* exists */
     or certificate 1[field.1.2.840.113635.100.6.2.6] /* exists */
        and certificate leaf[field.1.2.840.113635.100.6.1.13] /* exists */
        and certificate leaf[subject.OU] = "<Team ID>" )
```

Kiểm theo đúng hướng dẫn của TN3127: `codesign --display -r "MAS.req.txt" <bản App Store>` rồi `codesign --verify -vv -R "MAS.req.txt" <bản Developer ID>`, và làm ngược lại. Hai điểm chưa kiểm chứng: cờ của `codesign` để nhúng DR khi ký (tôi nhớ là `-r`, chưa mở `man codesign` để đối chiếu), và việc Apple sign lại bản App Store có giữ DR tuỳ ý hay không.

**Keychain: dùng chung.** `application-identifier` được tạo từ Team ID cộng bundle ID, và đó là nhóm truy cập mặc định của app. [Fact] [a-keychain-sharing]. Data protection keychain của app Mac Catalyst phân quyền theo nhóm này, không theo DR. [Fact] [a-tn3137]. Hai bản cùng Team ID và cùng bundle ID vì vậy đọc được cùng một key API [Suy luận]. Cần kiểm trên Mac vì hai bản dùng hai provisioning profile khác nhau.

**Cài cả hai trên một Mac.** `.pkg` do `dotnet publish` tạo cài app vào `/Applications` (theo research pipeline phát hành, task `CreateInstallerPackage`). App Store cũng cài vào `/Applications`. Hai bản cùng bundle ID và cùng tên app nên bản sau thay bản trước [Suy luận]. Hành vi của Launch Services khi có hai bản sao ở hai thư mục khác nhau: chưa kiểm chứng.

**`NSAppDataUsageDescription`:** khi app mở file trong container của "app khác" (trong mắt hệ thống), macOS hỏi người dùng và hiện thông điệp này; thiếu khoá thì dùng thông điệp mặc định. [Fact] [a-nsappdata]. Có thể thêm vào `Info.plist` để câu hỏi dễ hiểu hơn nếu vẫn còn hộp hội [Suy luận].

Nguồn: [Apple: Accessing files from the macOS App Sandbox][a-access], [Apple TN3127: Inside Code Signing: Requirements][a-tn3127], [Apple TN3137][a-tn3137], [Apple: Sharing access to keychain items among a collection of apps][a-keychain-sharing], [Apple: NSAppDataUsageDescription][a-nsappdata], [SDK: Codesign.cs][ios-codesign], [SDK: build properties][ios-build-props]

## Việc cần kiểm trên Mac (đề xuất, chưa kiểm chứng)

1. In ra `Environment.GetFolderPath(ApplicationData)` và `FileSystem.Current.AppDataDirectory` của app đã sandbox để chốt đường dẫn dữ liệu.
2. Chọn một PDF ở `~/Documents` và một ở `~/Downloads` bằng `FilePicker`: thử cả đọc qua `OpenReadAsync()` lẫn qua `FullPath` để biết entitlement nào đủ.
3. Gửi một câu chat và tạo một Bản soạn (HTTP đi) trên bản Release, với Console lọc `category:violation`.
4. Bấm "Sao chép" một Bản soạn và dán sang app khác; bấm một link `http` trong câu trả lời AI.
5. Nhập key ở bản Developer ID, cài bản Mac App Store (hoặc ngược lại): ghi lại có hộp hội xin quyền container không, app có mở được không, key còn đọc được không.
6. Chạy bốn lệnh `codesign` của TN3127 cho hai bản trước và sau khi đặt DR tường minh.

## Nguồn

[a-protect]: https://developer.apple.com/documentation/security/protecting-user-data-with-app-sandbox
[a-access]: https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox
[a-migrate]: https://developer.apple.com/documentation/security/migrating-your-app-s-files-to-its-app-sandbox-container
[a-diagnose]: https://developer.apple.com/documentation/security/discovering-and-diagnosing-app-sandbox-violations
[a-xcode-sandbox]: https://developer.apple.com/documentation/xcode/configuring-the-macos-app-sandbox
[a-ent-index]: https://developer.apple.com/documentation/bundleresources/security-entitlements
[a-ent-net]: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.network.client
[a-ent-usel-ro]: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.files.user-selected.read-only
[a-ent-usel-rw]: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.files.user-selected.read-write
[a-nshome]: https://developer.apple.com/documentation/foundation/nshomedirectory()
[a-docpicker]: https://developer.apple.com/documentation/uikit/uidocumentpickerviewcontroller
[a-startaccess]: https://developer.apple.com/documentation/foundation/nsurl/startaccessingsecurityscopedresource()
[a-nspasteboard]: https://developer.apple.com/documentation/appkit/nspasteboard
[a-nsappdata]: https://developer.apple.com/documentation/bundleresources/information-property-list/nsappdatausagedescription
[a-tn3137]: https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains
[a-tn3127]: https://developer.apple.com/documentation/technotes/tn3127-inside-code-signing-requirements
[a-keychain-sharing]: https://developer.apple.com/documentation/security/sharing-access-to-keychain-items-among-a-collection-of-apps
[a-ksec-dp]: https://developer.apple.com/documentation/security/ksecusedataprotectionkeychain
[ms-protecteddata]: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=net-11.0
[ms-securestorage]: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-11.0
[ms-filepicker]: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-11.0
[ms-launcher]: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/launcher?view=net-maui-11.0
[ms-fshelpers]: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-system-helpers?view=net-maui-11.0
[ms-mac-ent]: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/entitlements?view=net-maui-11.0
[ms-useshell]: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-11.0
[src-env-ios]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Private.CoreLib/src/System/Environment.iOS.cs
[src-env-unix]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Private.CoreLib/src/System/Environment.GetFolderPathCore.Unix.cs
[src-corelib-projitems]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Private.CoreLib/src/System.Private.CoreLib.Shared.projitems
[src-pal-searchpath]: https://github.com/dotnet/runtime/blob/release/11.0/src/native/libs/System.Native/pal_searchpath.m
[src-path-ios]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Private.CoreLib/src/System/IO/Path.Unix.iOS.cs
[src-protecteddata]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Security.Cryptography.ProtectedData/src/System/Security/Cryptography/ProtectedData.cs
[src-process]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs
[maui-essentials-csproj]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/Essentials.csproj
[maui-securestorage-apple]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/SecureStorage/SecureStorage.ios.tvos.watchos.macos.cs
[maui-securestorage-shared]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/SecureStorage/SecureStorage.shared.cs
[maui-securestorage-win]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/SecureStorage/SecureStorage.windows.cs
[maui-preferences-shared]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/Preferences/Preferences.shared.cs
[maui-filepicker-ios]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/FilePicker/FilePicker.ios.cs
[maui-fs-ios]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/FileSystem/FileSystem.ios.cs
[maui-fs-apple]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/FileSystem/FileSystem.ios.tvos.watchos.macos.cs
[maui-launcher-ios]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/Launcher/Launcher.ios.tvos.cs
[maui-browser-ios]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/Browser/Browser.ios.cs
[maui-clipboard-ios]: https://github.com/dotnet/maui/blob/net11.0/src/Essentials/src/Clipboard/Clipboard.ios.cs
[maui-template-ent]: https://github.com/dotnet/maui/blob/net11.0/src/Templates/src/templates/maui-mobile/Platforms/MacCatalyst/Entitlements.plist
[ios-compile-ent]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileEntitlements.cs
[ios-codesign]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/Codesign.cs
[ios-build-props]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/docs/building-apps/build-properties.md
