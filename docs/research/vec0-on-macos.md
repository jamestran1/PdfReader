# vec0 trên macOS arm64 và trong app sandbox (research cho #325)

Ngày: 2026-11-26. Ticket: #325, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

**Câu hỏi.** RAG phạm vi Toàn Workspace lưu vector trong bảng ảo `vec0` của extension SQLite sqlite-vec. Hiện repo chỉ có `native/vec0.dll` cho Windows, nạp qua `Microsoft.Data.Sqlite` (`src/PdfReaderApp/Services/SqliteDocumentIndex.cs:25-43`). Bản macOS của MAUI chạy trên Mac Catalyst, trong App Sandbox và Hardened Runtime. Ticket hỏi: (1) sqlite-vec có bản macOS arm64 không, license và phiên bản ra sao; (2) Mac Catalyst có nạp được extension động không, sandbox và library validation chặn đến đâu, dylib nằm ở đâu trong bundle và ký thế nào; (3) `vec0.dll` trong gói MSIX còn nạp được sau khi sang MAUI không; (4) quy mô thực tế là bao nhiêu và phương án dự phòng nào đủ.

**Cách làm.** Đọc mã nguồn repo (chỉ đọc). Tải asset phát hành của sqlite-vec và các gói NuGet liên quan về thư mục tạm để đọc header Mach-O và chạy `PRAGMA compile_options`. Đọc nguồn dyld (Apple), dotnet/macios, dotnet/efcore, SQLitePCL.raw; đọc tài liệu Apple, Microsoft Learn, SQLite. Số liệu trên mạng lấy ngày 2026-11-26. Nhận định gắn **[Fact]** (nguồn viết hoặc đo được) hoặc **[Suy luận]**; chưa có nguồn thì ghi "chưa kiểm chứng". Không có gì được chạy thử trên máy Mac.

## Tóm tắt

- **Có bản macOS arm64.** [Fact] GitHub Releases của `asg017/sqlite-vec` có `sqlite-vec-0.1.9-loadable-macos-aarch64.tar.gz` (ổn định mới nhất, 2026-03-31), bên cạnh bản `macos-x86_64`. Không có bản universal, không có asset riêng cho Mac Catalyst, cũng không có Windows arm64.
- **License song song MIT/Apache-2.0.** [Fact] `THIRD-PARTY-NOTICES.md` đang ghi MIT, hợp lệ, nhưng không ghi phiên bản.
- **`native/vec0.dll` trong repo là v0.1.6** [Fact]: trùng khớp từng byte với file trong asset `v0.1.6` (2024-11-20). Bản ổn định hiện là v0.1.9, có sửa lỗi `DELETE` và dọn chỗ trên bảng `vec0`.
- **Mac Catalyst không bị cấm nạp extension.** [Fact] `Microsoft.Data.Sqlite` 11.0.0 mang `libe_sqlite3.dylib` dựng riêng cho `maccatalyst-arm64`, có sẵn API nạp extension. dyld cho phép tiến trình Mac Catalyst `dlopen` dylib dựng cho macOS.
- **Không cần cờ biên dịch nào.** [Fact] SQLite chỉ có cờ tắt `SQLITE_OMIT_LOAD_EXTENSION`; `e_sqlite3` của repo không có cờ này (đã chạy `PRAGMA compile_options`).
- **Không cần entitlement `com.apple.security.cs.disable-library-validation`.** [Suy luận, tin cậy cao] Điều kiện: `vec0.dylib` giữ đuôi `.dylib`, nằm dưới `<App>.app/Contents/`, và vào bundle trong lúc build để SDK .NET ký cùng Team ID.
- **App Sandbox không chặn `dlopen` dylib trong bundle.** [Suy luận] Tài liệu Apple chỉ nói sandbox giới hạn truy cập tài nguyên qua entitlement.
- **Windows + MSIX: nạp như cũ.** [Suy luận, tin cậy cao] App hiện đã phát hành theo đúng cách này; MSIX chỉ cấm ghi vào thư mục cài đặt.
- **Quy mô nhỏ.** Thư viện mẫu trên máy dev có 5 tài liệu, khoảng 7.400 chunk, một model 1536 chiều; một truy vấn toàn Workspace quét khoảng 11,4 triệu phép nhân-cộng.
- **Dự phòng đủ dùng: quét vét cạn thuần C# trong bộ nhớ** với `TensorPrimitives.CosineSimilarity`. Các gói "vector store .NET" khác hoặc quay lại sqlite-vec, hoặc không lưu bền.
- **Kết luận cho ticket:** nhiều khả năng dùng được `vec0` trên macOS mà không phải tự dựng, nhưng chưa ai chạy thử. Cần một spike trên Mac mini (mục 5) trước khi viết ADR.

## 1. sqlite-vec cho macOS arm64: bản dựng, license, phiên bản

### 1.1 Bản dựng và nơi tải

**[Fact]** Nơi tải chính thức là GitHub Releases. Bản ổn định mới nhất là `v0.1.9` (2026-03-31). Các asset dạng loadable: `macos-aarch64`, `macos-x86_64`, `windows-x86_64`, `linux-aarch64`, `linux-x86_64`, `ios-aarch64`, `iossimulator-aarch64`, `iossimulator-x86_64`, `android-*`.

**[Fact]** Không có bản universal: mỗi tarball chứa đúng một `vec0.dylib` mỏng (bản aarch64 là arm64, bản x86_64 là x86_64). Map chỉ nhắm Apple Silicon nên chỉ cần `sqlite-vec-0.1.9-loadable-macos-aarch64.tar.gz`, SHA-256 của tarball là `8282126333399ddfe98bbbcc7a1936e7252625aac49df056a98be602e46bfd29` (khớp `checksums.txt` của release).

**[Fact]** Đọc header của `vec0.dylib` trong asset đó:

| Thuộc tính | Giá trị |
|---|---|
| Kích thước, SHA-256 | 161.896 byte, `193e480c50b59a55977d166f4aaf0e1bc8832d6963516e5950f39e4d2ce0b793` |
| Kiểu | `MH_DYLIB`, arm64 |
| `LC_BUILD_VERSION` | `platform=1 (MACOS)`, `minos=14.0.0`, `sdk=14.5.0` |
| Thư viện phụ thuộc | chỉ `/usr/lib/libSystem.B.dylib` |
| `LC_ID_DYLIB` (install name) | `dist/vec0.dylib` |
| Chữ ký | có `LC_CODE_SIGNATURE`, ad-hoc do linker tạo (flags `0x20002`), không có Team ID |
| Entry point | `sqlite3_vec_init` |

Asset `loadable-ios-aarch64` là nền tảng iOS (`LC_VERSION_MIN_IPHONEOS`), không dùng cho mục này.

**[Fact]** Quy trình phát hành của sqlite-vec: job `build-macos-aarch64-extension` chạy trên runner `macos-14` với `make loadable static`, không có `-target`. Riêng iOS được dựng bằng `-target arm64-apple-ios -isysroot ...`. Upstream không dựng cho Mac Catalyst.

### 1.2 License

**[Fact]** Repo có hai file `LICENSE-MIT` ("MIT License", "Copyright (c) 2024 Alex Garcia") và `LICENSE-APACHE`; GitHub ghi `spdx_id: Apache-2.0`. `THIRD-PARTY-NOTICES.md:53-55` của Trí Thư ghi "sqlite-vec (vec0.dll): License MIT". Chọn nhánh MIT là hợp lệ.

**[Suy luận, không phải tư vấn pháp lý]** MIT yêu cầu kèm thông báo bản quyền và văn bản license khi phân phối. `THIRD-PARTY-NOTICES.md` hiện chỉ ghi tên license và link, chưa có dòng bản quyền và phiên bản. Nên bổ sung khi thêm `vec0.dylib`.

### 1.3 Phiên bản trong repo

**[Fact]** `git log --oneline -- native/vec0.dll` chỉ có một commit: `2a77858` (2026-06-20). File dài 278.016 byte, SHA-256 `74e63ab75299804773676a5c757372472e797e03e02a2d8ff5fd91730dbd7531`, trùng khớp từng byte với `vec0.dll` trong `sqlite-vec-0.1.6-loadable-windows-x86_64.tar.gz` (phát hành 2024-11-20); trong binary có chuỗi `v0.1.6`. Repo không ghi phiên bản ở đâu.

**[Fact]** Các bản sau 0.1.6:
- `v0.1.7` (2026-03-17): "Proper `DELETE` support: Deleting vectors will now clear up space (if you DELETE enough to clear out a chunk, ~1024 vectors)"; thêm constraint trên cột `distance`; có fuzz testing.
- `v0.1.8` (2026-03-30): sửa gói NPM.
- `v0.1.9` (2026-03-31): sửa #274, `DELETE` trên bảng `vec0` có cột metadata text dài hơn 12 ký tự bị báo nhầm `SQLITE_DONE`.
- `v0.1.10-alpha.4` (2026-05-18): pre-release mới nhất, cũng là giá trị `VERSION` trên `main`. Repo không có commit mới từ 2026-05-18.

**[Suy luận]** Nên nâng `vec0.dll` lên 0.1.9 cho cả hai nền và ghi phiên bản. Lý do: thư viện mẫu đã có dấu hiệu chỗ không được dọn ở bản 0.1.6 (mục 4.2), và ADR 0008 đã thừa nhận `index.db` lớn dần. **Chưa kiểm chứng:** DB do 0.1.6 ghi có mở được bằng 0.1.9 không; cần chạy lại `SqliteDocumentIndexMigrationTests` và mở một bản sao DB thật.

Nguồn: https://github.com/asg017/sqlite-vec/releases , https://github.com/asg017/sqlite-vec/releases/tag/v0.1.9 , https://github.com/asg017/sqlite-vec/releases/tag/v0.1.7 , https://github.com/asg017/sqlite-vec/releases/tag/v0.1.6 , https://github.com/asg017/sqlite-vec/blob/main/.github/workflows/release.yaml , https://github.com/asg017/sqlite-vec/blob/main/LICENSE-MIT , https://github.com/asg017/sqlite-vec/blob/main/LICENSE-APACHE , `THIRD-PARTY-NOTICES.md`, `docs/adr/0005-store-only-msix-release.md`, `docs/adr/0008-multi-provider-ai-and-multi-model-vector-store.md`

## 2. Nạp extension động trên Mac Catalyst

### 2.1 Bundle SQLite có phần nạp extension không

**[Fact]** Repo đang dùng `Microsoft.Data.Sqlite` 10.0.9 (`src/PdfReaderApp/PdfReaderApp.csproj:31`), phụ thuộc `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11, tức `e_sqlite3` 3.49.1. Bản 11.0.0 (mới nhất trên NuGet, chỉ có `lib/net10.0`) phụ thuộc `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5; gói này kéo gói mới `SQLite` 3.53.4 (cùng tác giả Eric Sink) và `SQLitePCLRaw.config.e_sqlite3` 3.0.5.

**[Fact]** Gói `SQLite` 3.53.4 có `runtimes/maccatalyst-arm64/native/libe_sqlite3.dylib` (và `maccatalyst-x64`, `osx-*`, `win-*`). Gói `SQLitePCLRaw.config.e_sqlite3` có nhóm `net10.0-maccatalyst26.0` dùng provider động `SQLitePCLRaw.provider.e_sqlite3`; nguồn của provider này có P/Invoke `sqlite3_enable_load_extension`, `sqlite3_load_extension`, `sqlite3_db_config`.

**[Fact]** Về cờ biên dịch: tài liệu SQLite chỉ nêu một cờ liên quan, `SQLITE_OMIT_LOAD_EXTENSION` ("omits the entire extension loading mechanism from SQLite, including `sqlite3_enable_load_extension()` and `sqlite3_load_extension()` interfaces"). Trang đó không có cờ bật `SQLITE_ENABLE_LOAD_EXTENSION`. Đo trực tiếp trên `e_sqlite3.dll` 2.1.11 (win-x64, bản app đang dùng): `PRAGMA compile_options` không có mục `OMIT_*` nào, có `ENABLE_FTS5` và `THREADSAFE=1`. Chuỗi trong `libe_sqlite3.dylib` 3.53.4 (maccatalyst-arm64) có `dlopen` và `sqlite3_load_extension`, không có `OMIT_LOAD_EXTENSION`.

Kết luận: không cần thêm cờ biên dịch nào cho bundle SQLite.

### 2.2 Cách bật trong Microsoft.Data.Sqlite

**[Fact]** Theo nguồn nhánh `release/11.0` của `SqliteConnection.cs`:
- `EnableExtensions(true)`: nếu kết nối đang mở thì gọi `sqlite3_enable_load_extension(Handle, 1)`, nếu chưa mở thì chỉ ghi nhớ cờ.
- `LoadExtension(file, proc)`: nếu chưa gọi `EnableExtensions` thì gọi `sqlite3_db_config(Handle, SQLITE_DBCONFIG_ENABLE_LOAD_EXTENSION, 1, ...)` trước, rồi `sqlite3_load_extension`. Ghi lại để nạp lại khi kết nối mở lại.
- Đường dẫn thử: tham số truyền vào trước. Nếu tham số có phần thư mục thì chỉ thử đúng đường dẫn đó. Nếu chỉ là tên file thì thử thêm tiền tố `lib` (ngoài Windows) và các thư mục trong `NATIVE_DLL_SEARCH_DIRECTORIES`.
- Tài liệu Microsoft Learn lưu ý SQLite "calls the platform API directly to load libraries", nên có thể phải sửa `PATH`, `LD_LIBRARY_PATH`, `DYLD_LIBRARY_PATH`. Truyền đường dẫn tuyệt đối là cách chắc chắn nhất.

Code hiện tại (`SqliteDocumentIndex.cs:27-34`) đã đúng thứ tự: `Open()`, `EnableExtensions(true)`, `LoadExtension(vec0Path)` với đường dẫn tuyệt đối. Với `proc` là null, SQLite tự tìm `sqlite3_extension_init` rồi `sqlite3_vec_init` (suy từ tên file `vec0.dylib`); entry point này có trong dylib.

**[Fact]** Mặc định extension loading tắt. SQLite dùng `dlopen()` trên unix và `LoadLibrary()` trên Windows.

### 2.3 dyld có cho nạp dylib macOS trong tiến trình Mac Catalyst không

**[Fact]** Có. Hàm `UnsafeHeader::loadableIntoProcess` trong nguồn dyld ghi thẳng:

```cpp
bool iOSonMac = (processPlatform == Platform::macCatalyst);
...
// macOS dylibs can be loaded into iOSMac processes
if ( iOSonMac && this->builtForPlatform(Platform::macOS, true) )
    return true;
```

Ngoại lệ chỉ áp cho dylib dựng cho macOS (tham số thứ hai yêu cầu đúng một platform), không áp cho dylib iOS.

**[Suy luận, tin cậy cao]** `vec0.dylib` (platform MACOS, chỉ phụ thuộc libSystem) sẽ `dlopen` được trong app Mac Catalyst mà không cần tự dựng lại. Yêu cầu phụ: máy chạy macOS 14.0 trở lên (`minos` của dylib).

### 2.4 App Sandbox

**[Fact]** Apple: "App Sandbox provides protection to system resources and user data by limiting your app's access to resources requested through entitlements", và "To distribute a macOS app through the Mac App Store, you must enable the App Sandbox capability". Microsoft Learn: "On Mac Catalyst, .NET MAUI apps run in a sandbox that provides a set of rules that limit access between the app and system resources or user data."

**[Suy luận]** Tài liệu Apple không có quy tắc nào chặn nạp thư viện động từ trong bundle; sandbox giới hạn truy cập tài nguyên (file, mạng, IPC). `index.db` phải nằm trong container của app: việc này thuộc research thư mục dữ liệu, ngoài phạm vi ticket.

### 2.5 Hardened Runtime và library validation

**[Fact]** Apple: hardened runtime "enables library validation by default", chặn nạp thư viện "unless they're either signed by Apple or signed with the same Team ID as the main executable". "Apple only notarizes macOS apps that enable the Hardened Runtime capability." Entitlement `com.apple.security.cs.disable-library-validation` dành cho app "loads plug-ins that are signed by other third-party developers", và "Gatekeeper runs extra security checks on programs that have it disabled".

**[Fact]** SDK .NET tự ký dylib. `ComputeCodesignItems` quét app bundle: với macOS và Mac Catalyst, `dylibDirectory = Path.Combine(appPath, "Contents")` và "We find *.dylibs in any subdirectory". Các file này được ký bằng khoá của app, không mang entitlement của app, và "always signed" (dùng khoá `-` nếu chưa có khoá). Lệnh chạy là `codesign -v --force [-o runtime] [--timestamp] --sign <key>`. SDK cũng tự thêm `com.apple.security.cs.allow-jit` cho bản Release trên desktop.

**[Fact]** `vec0.dylib` upstream chỉ có chữ ký ad-hoc, không có Team ID, nên bắt buộc phải được ký lại.

**[Suy luận, tin cậy cao]** Không cần thêm `disable-library-validation`, với ba điều kiện:
1. File giữ đuôi `.dylib` (SDK chỉ tự tìm file theo đuôi này).
2. File nằm dưới `<App>.app/Contents/`.
3. File vào bundle trong lúc build, trước bước sign (không copy tay sau build, không nạp từ container người dùng).

### 2.6 Vị trí trong bundle

**[Fact]** Apple quy định "framework, dynamic library" đặt ở `Contents/Frameworks/` (áp cho cả Mac Catalyst); resource ở `Contents/Resources/`; executable chính ở `Contents/MacOS/`.

**[Fact]** Bộ SDK .NET đặt file theo metadata `PublishFolderType`:
- `*.dylib` mặc định được đoán là `DynamicLibrary`: "We will link with these libraries when linking the native executable". Không phù hợp với extension nạp lúc chạy.
- `PluginLibrary`: dành cho "plugins provided as an un-versioned dynamic library (*.so or *.dylib)... loaded on demand at runtime", không link. Cùng thư mục với assembly, tức `Contents/MonoBundle/` trên macOS và Mac Catalyst.
- `Content` đi đến `Contents/Resources/`.
- Tài liệu cảnh báo: "The App Store will reject any apps with *.dylib files (for iOS and tvOS, not for macOS or Mac Catalyst)."

**[Suy luận, chưa kiểm chứng]** Điểm khởi đầu cho csproj, chỉ áp cho TFM maccatalyst:

```xml
<None Include="..\..\native\macos-arm64\vec0.dylib"
      Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'"
      CopyToOutputDirectory="PreserveNewest" PublishFolderType="PluginLibrary" />
```

Vì assembly và dylib loại `PluginLibrary` chung một thư mục, `Path.Combine(AppContext.BaseDirectory, "vec0.dylib")` nhiều khả năng đúng trên Mac Catalyst. Cần in đường dẫn thật trong spike. `DefaultServices.CreateDocumentIndex` (`src/PdfReaderApp/ViewModels/DefaultServices.cs:34-36`) và các test đang ghi cứng tên `vec0.dll` (ví dụ `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexKnnTests.cs:22-23`), nên tách tên file theo nền tảng.

Nguồn: https://www.nuget.org/packages/Microsoft.Data.Sqlite/11.0.0 , https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5 , https://www.nuget.org/packages/SQLite/3.53.4 , https://www.nuget.org/packages/SQLitePCLRaw.config.e_sqlite3/3.0.5 , https://github.com/ericsink/SQLitePCL.raw/blob/main/src/SQLitePCLRaw.provider.e_sqlite3/Generated/provider_e_sqlite3_funcptrs_notwin.cs , https://github.com/dotnet/efcore/blob/release/11.0/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs , https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/extensions , https://www.sqlite.org/loadext.html , https://www.sqlite.org/compile.html , https://github.com/apple-oss-distributions/dyld/blob/main/mach_o/UnsafeHeader.cpp , https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.disable-library-validation , https://developer.apple.com/documentation/xcode/configuring-the-hardened-runtime , https://developer.apple.com/documentation/security/app-sandbox , https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/entitlements?view=net-maui-11.0 , https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.MacDev.Tasks/Tasks/ComputeCodesignItems.cs , https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.MacDev.Tasks/Tasks/Codesign.cs , https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.Shared/Xamarin.Shared.targets , https://developer.apple.com/documentation/bundleresources/placing-content-in-a-bundle , https://github.com/dotnet/macios/blob/main/dotnet/BundleContents.md , https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-items , https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-outside-app-store?view=net-maui-11.0

## 3. Windows sau khi sang MAUI và gói MSIX

### 3.1 Hiện trạng

**[Fact]** `src/PdfReaderApp/PdfReaderApp.csproj:16-19` chép `native/vec0.dll` cạnh exe bằng `None` + `CopyToOutputDirectory="PreserveNewest"`. `DefaultServices.cs:34-36` nạp từ `AppContext.BaseDirectory`. `pack-msix.ps1` chạy `dotnet publish -r win-x64 --self-contained true -o publish/win-x64` rồi `winapp package publish/win-x64`, tức `vec0.dll` nằm cạnh exe trong gói. ADR 0005 ghi rõ phát hành x64-only vì "`vec0.dll` trong repo chỉ có bản x64".

### 3.2 Ràng buộc của MSIX

**[Fact]** Tài liệu MSIX: gói cài vào `C:\Program Files\WindowsApps\<package_full_name>`; "After deployment, package files are marked read-only, and are heavily locked down by the operating system. Windows prevents apps from launching if those files are tampered with."; "Write inside the package: Not allowed." ADR 0005: Store tự ký lại gói.

**[Fact]** SQLite nạp extension bằng `LoadLibrary()` trên Windows.

**[Suy luận, tin cậy cao]** `vec0.dll` còn nạp như cũ sau khi sang MAUI. Đọc và nạp DLL từ thư mục cài đặt chỉ-đọc bằng đường dẫn tuyệt đối không vi phạm ràng buộc nào ở trên, và app hiện đã đi đúng đường này. Với MAUI, đặt `WindowsPackageType` để phát hành dạng gói MSIX; mọi file trong thư mục output đều vào gói.

Ràng buộc cần nhớ:
1. Không thể ghi hay cập nhật `vec0.dll` sau khi cài (thư mục chỉ đọc), nên phải đóng gói sẵn.
2. Chỉ x64: sqlite-vec không có `windows-arm64`, gói MSIX vẫn là x64. **Chưa kiểm chứng:** hành vi trên máy Windows ARM64 (chạy qua lớp giả lập x64).
3. File nằm trong bản đồ kiểm tra toàn vẹn của gói; sửa file làm app không khởi động.
4. `Microsoft.Data.Sqlite` 11.0.0 chỉ có `lib/net10.0`, dùng được cho `net11.0-windows`.

**[Suy luận]** Giữ item `None` có điều kiện TFM Windows cho `vec0.dll`. Tài liệu MAUI chỉ nói raw asset (`MauiAsset`) được chép vào gói, không cam kết nằm cạnh exe.

Nguồn: https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes , https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-cli , https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/single-project , `pack-msix.ps1`, `docs/adr/0005-store-only-msix-release.md`, `docs/RELEASE.md`

## 4. Quy mô thực tế và phương án dự phòng

### 4.1 Kích thước một Vector Index

**[Fact]** Từ mã nguồn:
- Chunk: `TextChunker.ChunkPages` cắt theo trang, tối đa 900 ký tự, chồng lấn 100 ký tự (`src/PdfReaderApp/Services/TextChunker.cs:23`). Lập chỉ mục theo lô 100 chunk (`DocumentIndexingService.cs:8`).
- Số chiều: mỗi số chiều một bảng `vec_chunks_<số chiều>`, tạo khi ghi (`EnsureVecTable`, `SqliteDocumentIndex.cs:467-474`). Mặc định OpenAI `text-embedding-3-small` vào `vec_chunks_1536`. Gemini `gemini-embedding-001` được yêu cầu trả 768 chiều (`AiModelCatalog.GeminiEmbeddingDimensions`), vào `vec_chunks_768`. Người dùng gõ tên model tự do, nên model 3072 chiều sẽ tạo `vec_chunks_3072`.
- Mọi vector được chuẩn hóa L2 (`NormalizingEmbeddingGenerator`), nên thứ hạng theo khoảng cách L2 của vec0 tương đương cosine.

### 4.2 Số liệu từ một thư viện mẫu

**[Fact]** Đọc bản sao của một `index.db` thật trên máy dev bằng `e_sqlite3` 2.1.11, chỉ đếm số dòng và kích thước, không đọc nội dung tài liệu. Số liệu chi tiết của thư viện cá nhân không ghi vào file công khai; dưới đây chỉ giữ phần cần cho ước lượng:

| Chỉ số | Giá trị |
|---|---|
| Tài liệu | 5 (tất cả `complete`) |
| Chunk | khoảng 7.400, trung bình 758 ký tự, tối đa 900 |
| Embedding Model đang có | `text-embedding-3-small` (1536 chiều) |
| Bảng `vec_chunks` của model đang dùng | khoảng 7.400 vector, 13 blob, khoảng 82 MB |
| Bảng `vec_chunks` của một model đã bỏ | 0 vector nhưng vẫn còn 6 blob, khoảng 19 MB |
| Kích thước file | 151 MB |

Dòng cuối cho thấy 0.1.6 không dọn chỗ sau khi xoá vector.

### 4.3 Truy vấn kNN hiện tại

**[Fact]** `RetrieveRelevantScored` (`SqliteDocumentIndex.cs:680-721`):

```sql
SELECT c.document_id, c.page_index, c.ordinal, c.text, v.distance
FROM vec_chunks_{dim} v
JOIN chunks c ON c.chunk_id = v.chunk_id
WHERE v.model_name = $m AND v.document_id = $id
  AND v.embedding MATCH $vec AND k = $k
ORDER BY distance
```

- Vector truyền vào dạng JSON `[f1,f2,...]`.
- Phạm vi Tài liệu này: `k = 5` (`RagContextService.BuildDocumentContextAsync`).
- Toàn Workspace: `BuildWorkspaceContextAsync` chạy truy vấn này cho từng Document đã `Complete`, mỗi Document lấy `TopK = 8`, rồi gộp theo `distance`, lấy 8 đoạn chung, cắt theo `MaxChars = 48.000` (`WorkspaceRagOptions`, `ContextLimits`).
- `model_name` và `document_id` là partition key, nên vec0 chỉ quét vector của một cặp (model, Document). vec0 quét vét cạn trong từng partition; binary có chuỗi "A LIMIT or 'k = ?' constraint is required on vec0 knn queries".

### 4.4 Hành vi khi không nạp được extension

**[Fact]** Từ mã nguồn: khi `_vecAvailable = false`, `WriteEmbeddings` không ghi; `DocumentIndexingService` đánh dấu `TextOnly` và không nhúng (`DocumentIndexingService.cs:79-83`); `RagContextService` không xếp lại lập chỉ mục cho các Document này (`RagContextService.cs:86`); `AnswerContextBuilder` dùng ngữ cảnh theo trang (`AnswerContextBuilder.cs:47`). FTS5 vẫn chạy. Hậu quả là mất RAG ngữ nghĩa và trích dẫn nguồn `[n]`, app không crash.

### 4.5 Phương án dự phòng

| Phương án | Đánh giá |
|---|---|
| A. Quét vét cạn thuần C# trong bộ nhớ | **[Suy luận] Đủ dùng, khuyến nghị nếu cần.** Không cần native library, không phụ thuộc ký số hay sandbox. Chi tiết bên dưới. |
| B. `CommunityToolkit.VectorData.InMemory` 1.0.1 | **[Fact]** MIT, provider trong bộ nhớ của `Microsoft.Extensions.VectorData`, "doesn't require any external service". Không lưu bền: vẫn phải tự lưu vector vào SQLite và nạp lại mỗi lần mở app. |
| C. `Microsoft.SemanticKernel.Connectors.SqliteVec` 1.74.0-preview | **[Fact]** MIT, preview. Phụ thuộc gói `sqlite-vec` 0.1.7-alpha.2.1 (chỉ có `linux-arm64`, `linux-x64`, `osx-arm64`, `osx-x64`, `win-x64`) và `Microsoft.Data.Sqlite` 9.0.10. Vẫn là vec0 nạp động, cũ hơn, không có RID maccatalyst: không giải quyết gì. |
| D. EF Core vector search | **[Fact]** EF 10 và 11 chỉ có vector search cho SQL Server và Cosmos DB; mục "SQLite" trong "What's new" EF 11 không có vector. Không có gói `Microsoft.Extensions.VectorData.SQLite` hay `CommunityToolkit.VectorData.Sqlite` trên NuGet. |
| E. Tự dựng `vec0.dylib` cho Mac Catalyst, hoặc nhúng tĩnh vec0 vào `e_sqlite3` tự build | **[Suy luận]** Chỉ cân nhắc khi spike ở mục 2 thất bại. Chi phí build và bảo trì cao. |

Chi tiết phương án A:
- Chi phí mỗi truy vấn toàn Workspace với một model: N × D phép nhân-cộng. Với thư viện mẫu: khoảng 7.400 × 1536 ≈ 11,4 triệu; bộ nhớ float32 ≈ 45,6 MB. Gấp mười lần (74.000 chunk): 114 triệu phép và 456 MB, lúc đó nên lưu int8 hoặc nạp theo Document. Model 3072 chiều tốn gấp đôi.
- Dùng `TensorPrimitives.CosineSimilarity` (có SIMD, thuộc `System.Numerics.Tensors`). **Chưa đo** thời gian trên máy thật.
- Giữ nguyên `IDocumentIndex`. Cài `RetrieveRelevantScored` bằng vòng lặp trên một `float[]` liên tục theo (model, số chiều), nạp lười và làm mới khi có ghi. Vector lưu ở bảng thường dạng BLOB thay vì bảng ảo `vec0`.

Nguồn: `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`, `src/PdfReaderApp/Services/RagContextService.cs`, `src/PdfReaderApp/Services/DocumentIndexingService.cs`, `src/PdfReaderApp/Services/TextChunker.cs`, `src/PdfReaderApp/Services/AiModelCatalog.cs`, `src/PdfReaderApp/Models/WorkspaceRagOptions.cs`, `src/PdfReaderApp/Services/AnswerContextBuilder.cs`, https://alexgarcia.xyz/sqlite-vec/features/vec0.html , https://learn.microsoft.com/en-us/dotnet/api/system.numerics.tensors.tensorprimitives.cosinesimilarity , https://learn.microsoft.com/en-us/dotnet/ai/vector-stores/how-to/use-vector-stores , https://www.nuget.org/packages/CommunityToolkit.VectorData.InMemory , https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.SqliteVec , https://www.nuget.org/packages/sqlite-vec , https://github.com/dotnet/EntityFramework.Docs/blob/main/entity-framework/core/what-is-new/ef-core-11.0/whatsnew.md

## 5. Việc cần làm trên Mac mini và các điểm chưa kiểm chứng

### 5.1 Spike, theo thứ tự

1. Thêm `vec0.dylib` 0.1.9 (aarch64) vào csproj như mục 2.6. Build Debug cho `net11.0-maccatalyst`, in `AppContext.BaseDirectory` và liệt kê `<App>.app/Contents`.
2. Chạy `SqliteDocumentIndexKnnTests` trên macOS (cần CI job macOS): xác nhận `LoadExtension` thành công.
3. Build có ký với `UseHardenedRuntime=true`: xác nhận `vec0.dylib` được ký cùng Team ID (`codesign -dv --verbose=4`) và app vẫn nạp được, không có `disable-library-validation`.
4. Build có sandbox (`com.apple.security.app-sandbox`), đúng bản dùng cho cả hai kênh phát hành: xác nhận `index.db` ghi được trong container và RAG chạy.
5. Nếu bước 2 hoặc 3 thất bại: ghi lại thông điệp lỗi của dyld, rồi mới tính phương án E hoặc A.

### 5.2 Chưa kiểm chứng

- Chưa có lần chạy nào trên Mac Catalyst.
- Giá trị `AppContext.BaseDirectory` và vị trí thật của `*.dylib` trong bundle do SDK .NET 11 tạo ra.
- Cú pháp item csproj ở mục 2.6 (theo tài liệu `BundleContents.md`, chưa build thử).
- Khả năng tương thích của DB ghi bởi vec0 0.1.6 khi mở bằng 0.1.9.
- Ngưỡng macOS tối thiểu của bản MAUI 11 Mac Catalyst so với `minos=14.0.0` của dylib.
- Hiệu năng của phương án A.

Nguồn: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-outside-app-store?view=net-maui-11.0 , https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-app-store?view=net-maui-11.0
