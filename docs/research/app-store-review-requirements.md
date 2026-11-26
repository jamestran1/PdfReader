# Yêu cầu review của Mac App Store với Trí Thư: privacy manifest, App Privacy, export compliance (research cho #334)

Ngày: 2026-11-26. Ticket: #334, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Trí Thư sắp có bản macOS (MAUI trên Mac Catalyst) đi cả kênh tải trực tiếp lẫn Mac App Store, dùng chung một bản đã bật App Sandbox. App không có tài khoản và không có máy chủ riêng; người dùng tự nhập API key của OpenAI, Gemini, Claude; app gửi đoạn chữ trích từ tài liệu và câu hỏi thẳng tới API đó; dữ liệu nằm trong SQLite và file PDF trong thư mục của app. Ticket hỏi review của Mac App Store yêu cầu khai báo gì: privacy manifest (loại dữ liệu, lý do dùng API, thư viện bên thứ ba, .NET và MAUI đã kèm chưa), App Privacy trên App Store Connect, export compliance (`ITSAppUsesNonExemptEncryption`), các điều khác của App Review Guidelines, và kênh tải trực tiếp có bắt buộc privacy manifest không.

Cách đọc: **[Fact]** là điều tài liệu viết hoặc tôi đo được (nguồn mở ngày 2026-11-26); **[Suy luận]** là nhận định của tôi, kèm mức tin cậy; "chưa kiểm chứng" là chỗ không tìm được nguồn chính hoặc chưa chạy trên Mac. Tôi không có máy Mac. Phần đo được làm trên Windows: tải gói NuGet và asset phát hành về thư mục tạm, liệt kê file trong gói, rồi quét tên ký hiệu trong file Mach-O. Cách quét này là heuristic (chỉ thấy ký hiệu còn trong bảng chuỗi). App Sandbox và chỗ lưu key API đã có research riêng (#327); file này chỉ nhắc lại khi ảnh hưởng tới khai báo.

## Tóm tắt

- **[Fact]** Tên file bắt buộc là `PrivacyInfo.xcprivacy`, không phải `PrivacyInfo.plist`; với macOS và Mac Catalyst đặt ở `<App>.app/Contents/Resources/` ([Apple][a-add]).
- **[Fact]** Apple chỉ bắt khai lý do dùng API (required reason API) cho iOS, iPadOS, tvOS, visionOS, watchOS; phần loại dữ liệu thu thập thì áp dụng "on all platforms" ([Apple][a-manifest]). **[Suy luận, cao]** Bản Mac Catalyst không bị từ chối vì thiếu khai báo lý do API, nhưng nên kèm một manifest để khớp với App Privacy và dùng chung cho cả hai kênh.
- **[Fact]** .NET 11 và MAUI 11 không sinh và không kèm manifest cho Mac Catalyst (đo trên `Microsoft.NETCore.App.Runtime.maccatalyst-arm64` 11.0.0, workload pack 27.2.11039, `Microsoft.Maui.Core` 11.0.10). Template MAUI chỉ có manifest cho iOS; docs của Microsoft chỉ nói iOS, iPadOS, tvOS.
- **[Fact]** Không gói nào của Trí Thư khớp danh sách SDK của Apple phải kèm manifest và chữ ký; PDFium, sqlite-vec, SkiaSharp, SQLite và các SDK AI đều không kèm manifest. **[Suy luận, cao]** App tự khai trong một manifest là đủ, không phải xin gì từ thư viện.
- **[Fact]** Mọi dylib đo được đều gọi `stat` hoặc `fstat` (nhóm File timestamp); `libSystem.Native.dylib` và `libe_sqlite3.dylib` gọi `statfs`, `fstatfs` (nhóm Disk space). Không dylib bản release nào đo được gọi `mach_absolute_time` hay `NSUserDefaults`. Lý do tương ứng: `C617.1` và `3B52.1`, `E174.1`, `35F9.1`.
- **[Fact]** App Privacy định nghĩa "Collect" là gửi dữ liệu ra khỏi thiết bị để nhà phát triển hoặc "third-party partners" (gồm third-party SDK) truy cập lâu hơn thời gian phục vụ request ([Apple][a-label]). **[Suy luận, cao]** Chữ trích từ tài liệu, câu hỏi và lịch sử chat gửi tới OpenAI, Gemini, Claude phải khai: `Other User Content` (và `Search History`), không tracking, chỉ dùng cho `App Functionality`. Ngoại lệ "optional disclosure" không áp dụng vì AI là chức năng chính.
- **[Suy luận, trung bình]** Nên trả lời là dữ liệu "linked to the user": mỗi request mang API key, tức account ID của người dùng phía provider. Chính API key không phải khai, vì Apple miễn "authentication token" gửi kèm request và không được lưu lại.
- **[Fact]** Export compliance: đặt `ITSAppUsesNonExemptEncryption` là `NO` trong `Info.plist` khi app chỉ dùng dạng mã hoá được miễn hồ sơ, ví dụ HTTPS qua API của hệ điều hành ([Apple][a-export]); key này dùng được cho macOS từ 10.0 ([Apple][a-itsapp]) và Microsoft hướng dẫn cùng cách cho Mac Catalyst ([Microsoft][ms-publish]). **[Suy luận]** App còn mang `BouncyCastle.Cryptography` (phụ thuộc của iText), nên nếu bán ở Pháp phải nộp "French encryption declaration" trên App Store Connect. Báo cáo tự phân loại hằng năm với BIS là nghĩa vụ pháp lý, Apple chỉ ghi "might be required".
- **[Fact]** Điều 5.1.2(i) bản hiện hành bắt app "clearly disclose where personal data will be shared with third parties, including with third-party AI, and obtain explicit permission before doing so" ([Apple][a-guidelines]). **[Fact]** Bản WPF hiện tại không có câu thông báo và bước đồng ý này, cũng không có link chính sách riêng tư trong app. Phải thêm cả hai.
- **[Fact]** App không có tài khoản nên điều 5.1.1(v) (xoá tài khoản) và 4.8 (đăng nhập bên thứ ba) không áp dụng; chỉ cần giữ cách xoá API key trong app.
- **[Fact]** Kênh tải trực tiếp: notary service kiểm chữ ký, hardened runtime, timestamp..., không nhắc tới privacy manifest ([Apple][a-notarize]). **[Suy luận, cao]** Không bắt buộc.
- **[Fact]** Việc khác phải làm: bỏ tên nền tảng khác khỏi app và metadata (2.3.10), tắt cơ chế báo bản mới ở bản Mac App Store (2.4.5(vii)), chuẩn bị cách reviewer thử tính năng AI (2.1), bỏ thuộc tính quarantine, trả lời bảng hỏi age rating.

## 1. Privacy manifest

### 1.1 Apple yêu cầu gì

- **[Fact]** Tên file bắt buộc là `PrivacyInfo.xcprivacy`. Với macOS và Mac Catalyst, file đặt ở `Contents/Resources/` trong bundle ([Apple: Adding a privacy manifest][a-add]). Ticket đang ghi `PrivacyInfo.plist`; khi cắt ticket làm cần đổi tên.
- **[Fact]** Manifest ghi hai phần với phạm vi khác nhau. Loại dữ liệu app hoặc SDK thu thập: "You need to provide this information for your app or third-party SDK on all platforms". Lý do dùng required reason API: "on iOS, iPadOS, tvOS, visionOS, and watchOS" ([Apple: Privacy manifest files][a-manifest], [Apple: Required reason API][a-reasons]). **[Suy luận, cao]** macOS và Mac Catalyst không thuộc diện phải khai lý do API. Tôi không tìm thấy tài liệu nào của Apple hay Microsoft nói app macOS bị từ chối vì thiếu `NSPrivacyAccessedAPITypes`.
- **[Fact]** Mốc và chế tài: từ 01/05/2024 app dùng required reason API mà không khai lý do thì không được App Store Connect nhận ([Apple: Required reason API][a-reasons]); từ 12/02/2025 app nộp review có SDK trong "SDKs that require a privacy manifest and signature" phải kèm manifest hợp lệ ([Apple: Adding a privacy manifest][a-add]). App Store Connect từ chối bản nộp có manifest không hợp lệ (khoá hoặc giá trị lạ), báo `ITMS-91056`; kiểm bằng `plutil -lint` ([Apple TN3181][a-tn3181]). Nên để ý: manifest sai còn nguy hiểm hơn không có manifest.

### 1.2 Thư viện bên thứ ba có cần manifest riêng không

- **[Fact]** Luật của Apple: SDK mà dùng required reason API thì phải tự khai trong manifest của nó, "can't rely on the privacy manifest files for apps that link the third-party SDK" ([Apple: Required reason API][a-reasons]). SDK nằm trong danh sách của Apple thì bắt buộc có manifest và chữ ký, "as well as any SDKs that repackage those on the list" ([Apple: Third-party SDK requirements][a-sdklist]). Đối chiếu danh sách đó: không có PDFium, sqlite-vec, SkiaSharp, SQLite, OpenAI, Anthropic, Google GenAI, Markdig, iText hay .NET. Danh sách có `BoringSSL / openssl_grpc`, `OpenSSL`, `Protobuf`, `grpcpp`, `sqflite`, `FMDB`; đó là tên các SDK gốc, không phải gói .NET thuần của Trí Thư.
- **[Fact]** Khuyến nghị của Microsoft: thành phần bên thứ ba "must provision their own privacy manifest files separately", và "Microsoft isn't responsible for any third-party privacy manifest" ([dotnet/macios: apple-privacy-manifest.md][ms-manifest]). Đo trên gói thật ngày 2026-11-26: không gói nào kèm file tên `privacy` hay `PrivacyInfo.xcprivacy`.

| Gói | Số file | File native có trong gói |
|---|---|---|
| [`Microsoft.NETCore.App.Runtime.maccatalyst-arm64` 11.0.0][nuget-coreclr] | 212 | `libcoreclr.dylib`, `libSystem.Native.dylib`, `libclrjit.dylib`, `libmscordaccore.dylib`... |
| [`Microsoft.MacCatalyst.Runtime.maccatalyst-arm64.net11.0_27.2` 27.2.11039][nuget-ios] | 33 | `libxamarin-dotnet-coreclr.dylib`, `libxamarin-dotnet.dylib`, `Microsoft.MacCatalyst.registrar.a`... |
| [`Microsoft.Maui.Core`][nuget-maui-core] và [`Microsoft.Maui.Essentials`][nuget-maui-ess] 11.0.10 | 47 và 36 | không có (chỉ assembly managed) |
| [`SkiaSharp.NativeAssets.MacCatalyst`][nuget-skia-cat] và [`.macOS`][nuget-skia-mac] 4.157.0 | 14 và 25 | `libSkiaSharp.framework` chỉ có `Resources/Info.plist` và `_CodeSignature`; `libSkiaSharp.dylib` |
| [`bblanchon.PDFium.macOS` 160.0.8179][nuget-pdfium] | 40 | `libpdfium.dylib` (osx-arm64, osx-x64) |
| [`SQLite` 3.53.4][nuget-sqlite] | 46 | `libe_sqlite3.dylib` (maccatalyst-arm64) |
| [`sqlite-vec` 0.1.9, asset macos-aarch64][vec0-release] | 1 | `vec0.dylib` |

- **[Fact]** `OpenAI` 2.11.0, `Anthropic` 13.3.0, `Google.GenAI` 1.27.0, `Microsoft.Extensions.AI.OpenAI` 10.7.0, `Markdig` 0.43.0, `CommunityToolkit.Mvvm` 8.4.2, `itext` 8.0.5: chỉ có file managed, không có file native và không có manifest (đo trong NuGet cache).
- **[Suy luận, cao]** Trí Thư không phải xin manifest từ thư viện nào; mọi khai báo nằm trong một manifest của app. Lý do: không SDK nào nằm trong danh sách bắt buộc, và các dylib chạm required reason API chỉ thuộc nhóm File timestamp và Disk space, mà hai nhóm này không bắt khai trên macOS.

### 1.3 .NET và MAUI trên Mac Catalyst đã sinh hay kèm manifest chưa

- **[Fact]** Chưa. Tìm mã trong `dotnet/macios` và `dotnet/maui` (nhánh mặc định): trong `dotnet/macios`, `PrivacyInfo` và `xcprivacy` chỉ xuất hiện ở bốn file docs; trong `dotnet/maui` chỉ xuất hiện ở ba template (đều dưới thư mục iOS), một app manual test và một integration test. Không có target MSBuild nào sinh manifest.
- **[Fact]** Template `maui-mobile` của MAUI có [`Platforms/iOS/Resources/PrivacyInfo.xcprivacy`][maui-template-pm] với dòng chú thích "This is the minimum required version of the Apple Privacy Manifest for .NET MAUI apps. The contents below are needed because of APIs that are used in the .NET framework and .NET MAUI SDK"; file khai `C617.1`, `35F9.1`, `E174.1`, còn `CA92.1` để trong chú thích ("only needed when you're using the Preferences API"). Thư mục [`Platforms/MacCatalyst`][maui-template-cat] của cùng template chỉ có `AppDelegate.cs`, `Entitlements.plist`, `Info.plist`, `Program.cs`, `SceneDelegate.cs`.
- **[Fact]** Docs của Microsoft: "All .NET Apps that target devices running iOS, iPadOS, or tvOS will require a `PrivacyInfo.xcprivacy` file in the app bundle", với ba nhóm `NSPrivacyAccessedAPICategoryFileTimestamp` (`C617.1`), `NSPrivacyAccessedAPICategorySystemBootTime` (`35F9.1`), `NSPrivacyAccessedAPICategoryDiskSpace` (`E174.1`) ([dotnet/macios: apple-privacy-manifest.md][ms-manifest], [Microsoft Learn: Apple privacy manifest][ms-manifest-learn]). Trang Microsoft Learn chỉ nói về iOS và iPadOS. Docs đó tự ghi là dựa trên tài liệu Apple "as of March 2024" và danh sách API chỉ "verified only for .NET versions 8.0.0 and later" ([dotnet/macios: required-reasons-bcl.md][ms-bcl]).

### 1.4 Nếu Trí Thư khai lý do API thì khai gì

Năm nhóm API của Apple ([Apple: NSPrivacyAccessedAPIType][a-apitype]): File timestamp (`stat`, `fstat`, `lstat`, `fstatat`, `getattrlist` và các biến thể, `NSFileCreationDate`...), System boot time (`systemUptime`, `mach_absolute_time`), Disk space (`statfs`, `statvfs`, `fstatfs`, `fstatvfs`, `getattrlist`..., `NSURLVolumeAvailableCapacityKey`...), Active keyboards (`activeInputModes`), User defaults (`NSUserDefaults`). Bảng dưới đối chiếu danh sách đó với ký hiệu đo được trong dylib.

| Nhóm API | Ai gọi (đo ngày 2026-11-26) | Lý do nên khai |
|---|---|---|
| File timestamp | `libSystem.Native.dylib` (`stat`, `fstat`, `lstat`); `libcoreclr.dylib`, `libmscordaccore.dylib`, `libxamarin-dotnet-coreclr.dylib`, `libSkiaSharp`, `libpdfium.dylib` (`stat`, `fstat`); `libe_sqlite3.dylib` (`stat`, `fstat`, `lstat`). Code app: `File.Copy` từ PDF người dùng chọn (`src/PdfReaderApp/Services/LibraryService.cs:50`), `File.Exists`, `File.ReadAllBytes` | `C617.1` (file trong container) và `3B52.1` (file người dùng cấp quyền qua hộp thoại mở) |
| Disk space | `libSystem.Native.dylib`, `libe_sqlite3.dylib` (`statfs`, `fstatfs`) | `E174.1` |
| System boot time | không dylib bản release nào đo được gọi `mach_absolute_time`; bảng của Microsoft chỉ ghi nhóm này cho Mono, cột CoreCLR trống ([dotnet/macios: required-reasons-bcl.md][ms-bcl]) | `35F9.1`, để khớp template MAUI và docs Microsoft |
| Active keyboards | không | không khai |
| User defaults | chỉ các biến thể `-debug` của `libxamarin-dotnet` có tham chiếu class `NSUserDefaults`; app lưu cài đặt bằng file JSON | `CA92.1` chỉ khi dùng `Preferences` của MAUI ([dotnet/macios: required-reasons-dotnet-maui.md][ms-maui-rr]) |

- **[Fact]** .NET ghi các API của bảng là "present in your app even if you do not explicitly call them" ([dotnet/macios: required-reasons-bcl.md][ms-bcl]). Kết quả đo của tôi không thấy ký hiệu `mach_absolute_time` trong dylib CoreCLR; hai điều này không mâu thuẫn vì bảng của Microsoft ghi nhóm System boot time cho Mono. **[Suy luận, cao]** Nên thêm một `PrivacyInfo.xcprivacy` vào bản Mac Catalyst dù Apple không bắt, vì ba lý do: App Privacy và manifest phải nói cùng một chuyện ("Refer to this report when you provide your app's privacy details in App Store Connect", [Apple: Describing data use][a-datause]); một file dùng được cho cả hai kênh; sau này nếu có bản iOS thì không phải làm lại. Chỉ dùng khoá Apple liệt kê, vì manifest sai là lỗi từ chối.

Nguồn: [Apple: Privacy manifest files][a-manifest], [Apple: Required reason API][a-reasons], [Apple: NSPrivacyAccessedAPIType][a-apitype], [Apple: Adding a privacy manifest][a-add], [Apple: Describing data use][a-datause], [Apple TN3181][a-tn3181], [Apple: Third-party SDK requirements][a-sdklist], [dotnet/macios: apple-privacy-manifest.md][ms-manifest], [dotnet/macios: required-reasons-bcl.md][ms-bcl], [dotnet/maui: template PrivacyInfo.xcprivacy][maui-template-pm].

## 2. App Privacy trên App Store Connect

### 2.1 Định nghĩa của Apple

- **[Fact]** Bắt buộc với mọi app: "This information is required to submit new apps and app updates to the App Store", và trả lời phải phủ cả "those of third-party partners whose code you integrate into your app" ([Apple: App privacy details][a-label], [App Store Connect Help: Manage app privacy][a-asc-privacy]).
- **[Fact]** "Collect" refers to transmitting data off the device in a way that allows you and/or your third-party partners to access it for a period longer than what is necessary to service the transmitted request in real time." "Third-party partners" refers to analytics tools, advertising networks, third-party SDKs, or other external vendors whose code you've added to your app." ([Apple: App privacy details][a-label]).
- **[Fact]** Dữ liệu chỉ xử lý trên máy thì không phải khai: "Data that is processed only on device is not 'collected' and does not need to be disclosed" ([Apple: App privacy details][a-label]).
- **[Fact]** Ngoại lệ "optional disclosure" chỉ áp dụng khi dữ liệu thoả cả bốn điều kiện, trong đó có: "Collection of the data occurs only in infrequent cases that are not part of your app's primary functionality, and which are optional for the user" và "the user affirmatively chooses to provide the data for collection each time" ([Apple: App privacy details][a-label]).
- **[Fact]** "Data linked to the user": "Data collected from an app is often linked to the user's identity, unless specific privacy protections are put in place before collection to de-identify or anonymize it" ([Apple: App privacy details][a-label]).
- **[Fact]** Thu thập vì lý do khác analytics hay quảng cáo thì vẫn phải khai: "if you collect data solely for the purpose of app functionality, declare the data on your label and indicate that it is only being used for that purpose" ([Apple: App privacy details][a-label]).

### 2.2 Áp vào Trí Thư

| Dữ liệu rời máy | Đi đâu | Cơ sở trong code | Khai trên App Store Connect |
|---|---|---|---|
| Đoạn chữ trích từ tài liệu (tạo embedding) | API của provider người dùng chọn, bằng key của người dùng | `src/PdfReaderApp/Services/DocumentIndexingService.cs:134`, `AiEmbeddingGeneratorFactory.cs` | `Other User Content` |
| Câu hỏi chat và câu hỏi tìm kiếm | API embedding và chat của provider | `src/PdfReaderApp/Services/RagContextService.cs:117` gửi `question` đi tạo embedding | `Other User Content` và `Search History` |
| Lịch sử chat của tài liệu | API chat của provider | `PRIVACY.md:20` mô tả hành vi này | gộp vào `Other User Content` |
| API key | chỉ gửi trong header của request tới chính provider đó | `src/PdfReaderApp/Services/AiChatClientFactory.cs` dựng client thẳng với `options.ApiKey` | không khai (xem dưới) |

- **[Fact]** Không có telemetry, crash report hay analytics nào trong `src/PdfReaderApp` (tìm `Telemetry`, `Analytics`, `Sentry`, `AppCenter`, `ApplicationInsights` bằng grep: không có kết quả). **[Suy luận, cao]** Không khai `Usage Data` hay `Diagnostics`.
- **[Suy luận, cao]** Dữ liệu gửi tới provider là "collected" theo định nghĩa của Apple: nó rời máy, người nhận là third-party SDK mà app nhúng, và nhà phát triển không thể bảo đảm bên nhận xoá ngay sau khi phục vụ request. Phải khai. Ngoại lệ "optional disclosure" không áp dụng: AI là chức năng chính của Trí Thư, và mỗi lượt chat là một lần gửi, không phải trường hợp "infrequent" mà người dùng xác nhận từng lần.
- **[Suy luận, trung bình]** `Search History` nên khai thêm: Apple định nghĩa là "Information about searches performed in the app" ([Apple: App privacy details][a-label]), và câu hỏi tìm kiếm theo ngữ nghĩa được gửi đi tạo embedding (`RagContextService.cs:117`). Khai thừa một loại dữ liệu không gây rủi ro review; khai thiếu thì có.
- **[Fact]** Apple hướng dẫn: với ô nhập văn bản tự do thì "Mark 'Other User Content' to represent generic free form text fields", và nhà phát triển không phải chịu trách nhiệm dự đoán mọi loại dữ liệu người dùng gõ vào ([Apple: App privacy details][a-label]). **[Suy luận]** Văn bản tài liệu và câu hỏi của Trí Thư thuộc trường hợp này, kể cả khi tài liệu chứa dữ liệu nhạy cảm.
- **[Suy luận, trung bình]** Nên trả lời "linked to the user" cho cả hai loại. Lập luận: request mang API key, mà Apple định nghĩa `User ID` là "account ID ... that can be used to identify a particular user or account"; provider gắn nội dung với tài khoản của người dùng; app không thể tách định danh ra vì key bắt buộc phải gửi. **Chưa kiểm chứng**: tôi không tìm thấy hướng dẫn riêng của Apple cho trường hợp người dùng tự mang key (BYO key). Cách đọc "nhà phát triển không nhận dữ liệu nên là Not Linked" cũng khả dĩ về logic, nhưng tôi không có nguồn chính nào xác nhận.
- **[Suy luận, cao]** API key không phải khai thành `User ID`: Apple miễn trường hợp "an authentication token or IP address is sent on a server call and not retained" ([Apple: App privacy details][a-label]). Key chỉ đi tới chính provider phát hành nó, để xác thực request. **[Fact]** Giá trị cho manifest: `NSPrivacyCollectedDataTypeOtherUserContent`, `NSPrivacyCollectedDataTypeSearchHistory`; `NSPrivacyCollectedDataTypeLinked` `true`; `NSPrivacyCollectedDataTypeTracking` `false`; `NSPrivacyCollectedDataTypePurposeAppFunctionality` ([Apple: NSPrivacyCollectedDataType][a-datatype], [Apple: Describing data use][a-datause]).

### 2.3 Cách trả lời trên App Store Connect

1. "Do you or your third-party partners collect data from this app?": **Yes**. Nếu chọn "No" thì không phải trả lời gì thêm ([App Store Connect Help: Manage app privacy][a-asc-privacy]).
2. Loại dữ liệu: **User Content, Other User Content**; **Search History**.
3. Với mỗi loại: tracking **No**; linked to you **Yes** (mục 2.2); purpose chỉ **App Functionality**, không tick `Analytics` hay `Product Personalization`.
4. **Privacy Policy URL** (bắt buộc với mọi app): một URL công khai ([App Store Connect Help: Manage app privacy][a-asc-privacy]). `PRIVACY.md` trong repo chưa đủ: phải là trang công khai và nội dung phải sửa (mục 4 và checklist). "User Privacy Choices URL" là tuỳ chọn ([Apple: App privacy details][a-label]).
5. Trả lời có thể sửa bất cứ lúc nào mà không cần gửi bản cập nhật ([Apple: App privacy details][a-label]); giữ App Privacy, manifest và chính sách riêng tư khớp nhau khi đổi hành vi.

Nguồn: [Apple: App privacy details][a-label], [App Store Connect Help: Manage app privacy][a-asc-privacy], [Apple: Describing data use][a-datause], [Apple: NSPrivacyCollectedDataType][a-datatype].

## 3. Export compliance

### 3.1 Apple hỏi gì

- **[Fact]** Khi upload lên TestFlight hoặc App Store là upload lên máy chủ ở Mỹ; "If your app uses, accesses, contains, implements, or incorporates encryption, this is considered an export of encryption software" ([Apple: Complying with Encryption Export Regulations][a-export]).
- **[Fact]** Ý nghĩa của key `ITSAppUsesNonExemptEncryption`: đặt `NO` nếu app, "including any third-party libraries it links against", không dùng mã hoá hoặc chỉ dùng dạng miễn; đặt `YES` nếu dùng mã hoá không miễn. "Typically, the use of encryption that's built into the operating system, for example, when your app makes HTTPS connections using URLSession, is exempt from export documentation upload requirements, whereas the use of proprietary encryption is not." Key có mặt từ macOS 10.0. Nếu `Info.plist` không có key này thì App Store Connect hỏi bảng câu hỏi mỗi lần upload phiên bản mới ([Apple: ITSAppUsesNonExemptEncryption][a-itsapp], [Apple: Complying with Encryption Export Regulations][a-export]); build thiếu thông tin này ở trạng thái "Missing Compliance" cho tới khi trả lời ([App Store Connect Help: Provide export compliance information][a-beta-export]).
- **[Fact]** Bảng của App Store Connect Help ([App Store Connect Help: Export compliance documentation for encryption][a-encdoc]):

| Thuật toán app dùng | Hồ sơ phải nộp trên App Store Connect |
|---|---|
| chỉ dùng mã hoá trong hệ điều hành Apple | không phải nộp gì |
| thuật toán chuẩn (industry standard) không do hệ điều hành Apple cung cấp | nộp "French encryption declaration", chỉ khi phân phối trên App Store ở Pháp |
| thuật toán độc quyền hoặc không được cơ quan chuẩn quốc tế chấp nhận | nộp CCATS của Mỹ và khai báo Pháp |

- **[Fact]** Apple nhắn: "it's your responsibility to review the Export Administration Regulation to determine whether your app's use of encryption requires a formal classification (CCATS) from BIS", và "you're responsible for all liabilities associated with misinterpretation of export regulations or claiming exemption inaccurately" ([App Store Connect Help: Overview of export compliance][a-asc-export]). Apple còn ghi: "If your app uses exempt forms of encryption, you might alternatively be required to submit a year-end self-classification report to the U.S. government" ([Apple: Complying with Encryption Export Regulations][a-export]).

### 3.2 Áp vào Trí Thư

- **[Fact]** HTTPS: .NET 11 trên nền Apple thực hiện TLS bằng crypto của Apple. Nhánh `release/11.0` của `SslStreamPal.OSX.cs` dùng `Interop.AppleCrypto`, có chú thích "SecureTransport is okay with a 0 byte input" ([dotnet/runtime: SslStreamPal.OSX.cs][rt-ssl]); gói runtime mang `libSystem.Net.Security.Native.dylib` và `libSystem.Security.Cryptography.Native.Apple.dylib`. Key API trên macOS: `SecureStorage` của MAUI lưu vào Keychain (`SecKeyChain.Add`, `SecKeyChain.QueryAsRecord`) ([dotnet/maui: SecureStorage][maui-securestorage]); research #327 đề xuất dùng `SecureStorage` cho bản Mac Catalyst.
- **[Suy luận, cao]** Với HTTPS và Keychain, app chỉ dùng "encryption limited to that within the Apple operating system": đặt `ITSAppUsesNonExemptEncryption` là `NO`, không phải nộp hồ sơ, không có vòng review encryption.
- **[Fact]** App còn mang một thư viện mã hoá managed: `itext.bouncy-castle-adapter` 8.0.5 phụ thuộc `BouncyCastle.Cryptography` 2.2.1 (`src/PdfReaderApp/PdfReaderApp.csproj:27`), và `BouncyCastle.Cryptography.dll` có trong output build của repo. PDFium cũng nhận tham số mật khẩu (`FPDF_LoadDocument(FPDF_STRING file_path, FPDF_BYTESTRING password)` trong `fpdfview.h` của gói 160.0.8179), tức có khả năng giải mã PDF.
- **[Suy luận, trung bình]** Vì vậy app "contains or incorporates" thuật toán chuẩn không do hệ điều hành cung cấp. Hệ quả: nếu chọn phân phối ở Pháp, phải nộp "French encryption declaration" trên App Store Connect; không bán ở Pháp thì không phải nộp gì. Không cần CCATS vì không có thuật toán độc quyền. Nếu map quyết định bỏ iText (theo #321, #324) thì phần BouncyCastle biến mất, nhưng thư viện đọc PDF thay thế vẫn giải mã PDF có mật khẩu, nên không nên dựa vào việc bỏ iText để thoát hàng này.

### 3.3 Báo cáo tự phân loại với BIS

- **[Fact]** 15 CFR 740.17(e)(3) (bản eCFR ngày 01/11/2026): báo cáo tự phân loại áp dụng cho hàng mã hoá "mass market" và hàng không mass market được xuất theo (b)(1) sau khi tự phân loại; phải gửi cho BIS và ENC Encryption Request Coordinator "no later than February 1 the following year"; "Each product must be included in a report only one time", nhưng nếu không có sản phẩm mới trong năm vẫn phải gửi email báo "nothing has changed" ([eCFR: 15 CFR 740.17][ear-74017]). **[Suy luận]** Đây là nghĩa vụ pháp lý với chính phủ Mỹ, không phải điều kiện review của Apple; Apple ghi "might be required" và giao trách nhiệm cho nhà phát triển. Tôi không kết luận Trí Thư có thuộc diện phải báo cáo hay không; đó là câu hỏi pháp lý. Nên ghi nhận trong ADR phát hành để chủ repo quyết.

Nguồn: [Apple: Complying with Encryption Export Regulations][a-export], [Apple: ITSAppUsesNonExemptEncryption][a-itsapp], [App Store Connect Help: Overview of export compliance][a-asc-export], [App Store Connect Help: Export compliance documentation][a-encdoc], [App Store Connect Help: Provide export compliance information][a-beta-export], [Microsoft Learn: Publish a Mac Catalyst app][ms-publish], [dotnet/runtime: SslStreamPal.OSX.cs][rt-ssl], [dotnet/maui: SecureStorage][maui-securestorage], [eCFR: 15 CFR 740.17][ear-74017].

## 4. Yêu cầu khác của App Review Guidelines

Trích từ bản hiện hành ngày 2026-11-26 ([Apple: App Review Guidelines][a-guidelines]).

| Điều | Áp dụng với Trí Thư? | Phải làm |
|---|---|---|
| 5.1.1(i) Privacy Policies: "All apps must include a link to their privacy policy in the App Store Connect metadata field and within the app in an easily accessible manner" | có | Thêm link chính sách riêng tư trong app. Bản WPF hiện tại không có (`PRIVACY.md` chỉ nằm trong repo) |
| 5.1.1(i): chính sách phải nêu dữ liệu thu thập, cách thu, mọi mục đích dùng; xác nhận bên thứ ba được chia sẻ dữ liệu (gồm third-party SDK) bảo vệ dữ liệu ngang bằng; "Explain its data retention/deletion policies and describe how a user can revoke consent and/or request deletion" | có | Sửa `PRIVACY.md`: hiện chỉ nhắc OpenAI (`PRIVACY.md:11-22`) trong khi app hỗ trợ Gemini và Claude (`SettingsWindow.xaml:54-63`); chỉ nói Windows và DPAPI (`PRIVACY.md:11`, `:26`); chưa nêu xoá và thu hồi dữ liệu trên macOS |
| 5.1.1(ii) Permission: phải có consent để thu thập dữ liệu, và "an easily accessible and understandable way to withdraw consent"; "Paid functionality must not be dependent on or require a user to grant access to this data" | có | Thoả nếu tính năng đọc PDF không cần key và xoá key là ngừng mọi gửi dữ liệu. `WindowsSettingsService.cs:51-54` đã xoá file key khi lưu chuỗi rỗng |
| 5.1.1(v) Account Sign-In: app không có tính năng tài khoản thì phải dùng được không cần đăng nhập; hỗ trợ tạo tài khoản thì phải cho xoá tài khoản trong app; "Apps may not require users to enter personal information to function, except when directly relevant to the core functionality" | một phần | Không có tài khoản nên không cần xoá tài khoản (trang hỗ trợ của Apple: "Apps submitted to the App Store that support account creation must also let people initiate deletion of their account within the app", [Apple: Offering account deletion][a-acctdel]). Phải giữ cách dùng app không cần key, và cách thu hồi key |
| 5.1.2(i): "You must clearly disclose where personal data will be shared with third parties, including with third-party AI, and obtain explicit permission before doing so" | có | Thêm thông báo rõ và một bước đồng ý rõ trước lần gửi đầu tiên (xem checklist). Hiện `SettingsWindow.xaml:50` chỉ ghi "Key được mã hoá và lưu cục bộ (DPAPI)." |
| 5.1.2(ii): dữ liệu thu cho mục đích này không được dùng cho mục đích khác | không vi phạm | Không có gì để làm |
| 1.2 User-Generated Content: app cho người dùng tạo nội dung, gồm "AI content creation apps", phải có lọc, báo cáo, chặn người lạm dụng, và "Published contact information so users can easily reach you" | rủi ro thấp | **[Suy luận]** Điều này viết cho dịch vụ mà người dùng chia sẻ nội dung với nhau; nội dung của Trí Thư nằm trên máy người dùng và chat chỉ với AI. Nên công bố thông tin liên hệ và giải thích trong Notes for Review |
| 4.8 Login Services: dùng dịch vụ đăng nhập bên thứ ba để tạo "primary account" với app | không | **[Suy luận, cao]** API key không phải dịch vụ đăng nhập và không tạo tài khoản với Trí Thư. Không cần Sign in with Apple |
| 2.1(a) và Before You Submit: "Provide App Review with full access to your app"; nếu không cấp được tài khoản demo vì lý do pháp lý hoặc bảo mật thì có thể dựng demo mode "with prior approval by Apple" | có | **[Suy luận]** Reviewer không có API key thì không thử được tính năng AI. Nên ghi trong App Review Information rằng AI cần key của người dùng, và cấp một key thử có hạn mức cho reviewer |
| 2.3 và 2.3.10: metadata (gồm "privacy information") phải phản ánh đúng trải nghiệm; "don't include names, icons, or imagery of other mobile platforms or alternative app marketplaces in your app or metadata" | có | Bản macOS không được nêu Windows hay Microsoft Store trong mô tả, ảnh chụp, giới thiệu. Mô tả phải nói rõ AI cần API key của người dùng |
| 2.4.5(i): app trên Mac App Store "must be appropriately sandboxed" | đã có quyết định | Xem #327. Template MAUI ghi chú "App Sandbox must be enabled to distribute a MacCatalyst app through the Mac App Store" và `com.apple.security.network.client` là bắt buộc để mở kết nối đi ([dotnet/maui: Entitlements.plist][maui-template-ent], [Microsoft Learn: Publish a Mac Catalyst app][ms-publish]) |
| 2.4.5(ii) và 2.5.2: gói và nộp bằng công nghệ của Xcode; tự chứa, "may not read or write data outside the designated container area"; không tải mã về | có | Đúng với thiết kế hiện tại (dữ liệu trong container, `vec0.dylib` nằm trong bundle). Thuộc #322 và #327 |
| 2.4.5(vi): "may not present a license screen at launch, require license keys, or implement their own copy protection" | rủi ro thấp | **[Suy luận, cao]** API key là uỷ quyền của người dùng bên provider, không phải key mở khoá tính năng của Trí Thư: app chạy đầy đủ tính năng đọc PDF mà không cần key |
| 2.4.5(vii): "They must use the Mac App Store to distribute updates; other update mechanisms are not allowed" | có | **[Suy luận, trung bình]** Map đã quyết định bản tải trực tiếp báo có bản mới và mở trang tải. Cùng một binary mà lên Mac App Store thì cơ chế đó phải tắt trong bản Mac App Store, hoặc tách thành hai bản publish |
| 2.4.5(viii), 2.5.1: chạy trên OS hiện hành; chỉ dùng API công khai; không dùng công nghệ lỗi thời hoặc cài thêm | có | .NET 11 workload dùng Xcode 27.2 theo #322; runtime nằm trong bundle |
| 3.1.1 In-App Purchase: tính năng mở khoá trong app phải dùng IAP | không | **[Suy luận, cao]** Trí Thư không bán gì trong app; AI dùng key người dùng tự mua ngoài app. Nên nói rõ trong Notes for Review để tránh bị hỏi |
| 4.2 Minimum Functionality | không | App có tính năng riêng, không phải web đóng gói |

Ngoài Guidelines, App Store Connect còn các yêu cầu nộp bản macOS như sau ([Apple: Upcoming Requirements][a-upcoming]):

- **[Fact]** Bỏ thuộc tính `com.apple.quarantine` khỏi mọi file trong app trước khi upload (từ 18/02/2025).
- **[Fact]** Trả lời bảng hỏi age rating mới ("An Unrated app can't be published on the App Store", [App Store Connect Help: Age ratings][a-agerating]) và khai trader status nếu phân phối ở EU: app không có trader status sẽ bị gỡ khỏi App Store ở EU cho tới khi khai và được xác minh, theo Digital Services Act.
- **[Fact]** Bản upload phải dựng bằng Xcode 26 trở lên; mốc này liệt kê SDK cho iOS, iPadOS, tvOS, visionOS, watchOS, không nêu macOS.

Nguồn: [Apple: App Review Guidelines][a-guidelines], [Apple: Offering account deletion][a-acctdel], [Apple: Upcoming Requirements][a-upcoming], [App Store Connect Help: Age ratings][a-agerating], [Microsoft Learn: Publish a Mac Catalyst app][ms-publish], [dotnet/maui: Entitlements.plist][maui-template-ent].

## 5. Kênh tải trực tiếp có bắt buộc privacy manifest không

- **[Fact]** Notarization không phải App Review: "The Apple notary service is an automated system that scans your software for malicious content, checks for code-signing issues, and returns the results to you quickly" ([Apple: Notarizing macOS software][a-notarize]). Danh sách yêu cầu của notary service: ký mọi executable, chứng chỉ Developer ID, Hardened Runtime, secure timestamp, không có `com.apple.security.get-task-allow`, SDK macOS 10.9 trở lên, entitlements định dạng đúng ([Apple: Notarizing macOS software][a-notarize], [Apple: Resolving common notarization issues][a-notary-issues]). Không có dòng nào về privacy manifest, App Privacy hay export compliance.
- **[Fact]** Hai mốc về manifest đều gắn với App Store Connect: "aren't accepted by App Store Connect" ([Apple: Required reason API][a-reasons]) và "apps you submit for review in App Store Connect must contain a valid privacy manifest file" ([Apple: Adding a privacy manifest][a-add]).
- **[Suy luận, cao]** Kênh tải trực tiếp không bắt buộc privacy manifest. Vì hai kênh dùng chung một bản, file manifest và khoá `ITSAppUsesNonExemptEncryption` sẽ đi theo cả hai; không có hại gì.

Nguồn: [Apple: Notarizing macOS software][a-notarize], [Apple: Resolving common notarization issues][a-notary-issues], [Apple: Required reason API][a-reasons], [Apple: Adding a privacy manifest][a-add].

## Việc phải thêm vào project và phải khai báo

Project (làm trước khi nộp):

- [ ] Thêm `Platforms/MacCatalyst/PrivacyInfo.xcprivacy` (hoặc vị trí tương đương của project MAUI), đưa vào `Contents/Resources/` của bundle, rồi kiểm bằng `plutil -lint` ([Apple TN3181][a-tn3181]). **Chưa kiểm chứng** cách đưa file vào đúng chỗ với SDK .NET cho Mac Catalyst: docs của Microsoft chỉ hướng dẫn cho iOS (`BundleResource` với `LogicalName="PrivacyInfo.xcprivacy"`); sau khi publish phải liệt kê bundle để xác nhận.
- [ ] Nội dung manifest (chỉ dùng khoá Apple liệt kê; không có `NSPrivacyTrackingDomains`):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>NSPrivacyTracking</key><false/>
    <key>NSPrivacyCollectedDataTypes</key>
    <array>
        <dict>
            <key>NSPrivacyCollectedDataType</key><string>NSPrivacyCollectedDataTypeOtherUserContent</string>
            <key>NSPrivacyCollectedDataTypeLinked</key><true/>
            <key>NSPrivacyCollectedDataTypeTracking</key><false/>
            <key>NSPrivacyCollectedDataTypePurposes</key><array><string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string></array>
        </dict>
        <dict>
            <key>NSPrivacyCollectedDataType</key><string>NSPrivacyCollectedDataTypeSearchHistory</string>
            <key>NSPrivacyCollectedDataTypeLinked</key><true/>
            <key>NSPrivacyCollectedDataTypeTracking</key><false/>
            <key>NSPrivacyCollectedDataTypePurposes</key><array><string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string></array>
        </dict>
    </array>
    <key>NSPrivacyAccessedAPITypes</key>
    <array>
        <dict>
            <key>NSPrivacyAccessedAPIType</key><string>NSPrivacyAccessedAPICategoryFileTimestamp</string>
            <key>NSPrivacyAccessedAPITypeReasons</key><array><string>C617.1</string><string>3B52.1</string></array>
        </dict>
        <dict>
            <key>NSPrivacyAccessedAPIType</key><string>NSPrivacyAccessedAPICategoryDiskSpace</string>
            <key>NSPrivacyAccessedAPITypeReasons</key><array><string>E174.1</string></array>
        </dict>
        <dict>
            <key>NSPrivacyAccessedAPIType</key><string>NSPrivacyAccessedAPICategorySystemBootTime</string>
            <key>NSPrivacyAccessedAPITypeReasons</key><array><string>35F9.1</string></array>
        </dict>
    </array>
</dict>
</plist>
```

- [ ] Thêm `ITSAppUsesNonExemptEncryption` = `false` vào `Info.plist` của Mac Catalyst ([Apple: ITSAppUsesNonExemptEncryption][a-itsapp], [Microsoft Learn: Publish a Mac Catalyst app][ms-publish]).
- [ ] Thêm bước đồng ý rõ trước lần gửi dữ liệu đi đầu tiên (5.1.2(i)): câu thông báo nêu tên provider, nêu dữ liệu được gửi (đoạn chữ của tài liệu, câu hỏi, lịch sử chat), một nút đồng ý, lưu trạng thái; không gửi gì khi chưa đồng ý.
- [ ] Thêm link sang chính sách riêng tư ở chỗ dễ thấy trong app (5.1.1(i)).
- [ ] Sửa câu "Key được mã hoá và lưu cục bộ (DPAPI)." cho bản macOS (`SettingsWindow.xaml:50`), vì bản Mac dùng Keychain.
- [ ] Tắt cơ chế báo bản mới và mở trang tải ở bản Mac App Store (2.4.5(vii)), hoặc xuất hai bản publish khác nhau.
- [ ] Trước khi upload: rà text và metadata của bản macOS để không còn "Windows", "Microsoft Store" (2.3.10); xoá `com.apple.quarantine` trên mọi file của app.
- [ ] Cập nhật `PRIVACY.md` cho macOS: đủ ba provider; nêu chỗ lưu key trên Mac; nêu cách xoá dữ liệu (xoá tài liệu khỏi thư viện) và thu hồi đồng ý (xoá key); bỏ câu chỉ đúng với Windows ("Gỡ Trí Thư khỏi Windows sẽ xoá thư mục dữ liệu").

Khai báo trên App Store Connect:

- [ ] App Privacy: có thu thập dữ liệu; `Other User Content` và `Search History`; không tracking; linked to you; chỉ `App Functionality`.
- [ ] Privacy Policy URL công khai.
- [ ] Export compliance: app chỉ dùng mã hoá miễn (đã có trong `Info.plist`); quyết định có bán ở Pháp hay không, nếu có thì nộp French encryption declaration.
- [ ] Age rating và trader status (nếu phân phối ở EU).
- [ ] App Review Information: cách thử tính năng AI (key thử có hạn mức), lời giải thích rằng app không có tài khoản, AI dùng key của người dùng, không có IAP, nội dung không chia sẻ giữa người dùng.

## Chưa kiểm chứng

- Apple có quét dylib trong app macOS để tìm required reason API hay không, và App Store Connect có đọc `PrivacyInfo.xcprivacy` đặt ở `Contents/Resources/` của app Catalyst hay không. Tài liệu chỉ nêu năm nền iOS, iPadOS, tvOS, visionOS, watchOS; không có câu nào nói "macOS không cần".
- "Linked" hay "Not Linked" cho trường hợp BYO key: không có hướng dẫn riêng của Apple. Cũng chưa rõ mỗi provider (OpenAI, Anthropic, Google) giữ dữ liệu API bao lâu; tôi không đọc chính sách của ba bên trong research này. Nếu một provider không lưu lại thì dữ liệu gửi tới bên đó không thoả định nghĩa "Collect" của Apple.
- Thủ tục nộp French encryption declaration (mẫu, nơi nộp), và Trí Thư có thuộc diện báo cáo tự phân loại với BIS hay không (câu hỏi pháp lý).
- `bblanchon.PDFium.macOS` có chứa một phần BoringSSL dựng tĩnh hay không (BoringSSL có tên trong danh sách SDK của Apple). Quét chuỗi trong `libpdfium.dylib` không tìm thấy ký hiệu `EVP_*`; nếu Apple báo `ITMS-91061` khi upload thì xử lý lúc đó.
- `Anthropic` 13.3.0 có chuỗi header `x-stainless-os`, `x-stainless-arch`, `x-stainless-runtime`... (đo trong `Anthropic.dll`), tức SDK báo hệ điều hành và kiến trúc tới Anthropic kèm mỗi request. `OpenAI` 2.11.0 không có các header `x-stainless-*` này. Tôi chưa tìm được cách tắt, và chưa thấy tài liệu nào của Apple xếp nhóm header này vào loại dữ liệu phải khai; chưa kiểm chứng thêm.
- Khi gỡ app khỏi macOS, container và dữ liệu có bị xoá hay không; ảnh hưởng tới câu "cách xoá dữ liệu" trong chính sách riêng tư.

## Nguồn

Định nghĩa của các link tham chiếu dùng trong file.

[a-manifest]: https://developer.apple.com/documentation/bundleresources/privacy-manifest-files
[a-reasons]: https://developer.apple.com/documentation/bundleresources/describing-use-of-required-reason-api
[a-apitype]: https://developer.apple.com/documentation/bundleresources/app-privacy-configuration/nsprivacyaccessedapitypes/nsprivacyaccessedapitype
[a-add]: https://developer.apple.com/documentation/bundleresources/adding-a-privacy-manifest-to-your-app-or-third-party-sdk
[a-datause]: https://developer.apple.com/documentation/bundleresources/describing-data-use-in-privacy-manifests
[a-datatype]: https://developer.apple.com/documentation/bundleresources/app-privacy-configuration/nsprivacycollecteddatatypes/nsprivacycollecteddatatype
[a-tn3181]: https://developer.apple.com/documentation/technotes/tn3181-debugging-invalid-privacy-manifest
[a-sdklist]: https://developer.apple.com/support/third-party-SDK-requirements/
[a-label]: https://developer.apple.com/app-store/app-privacy-details/
[a-asc-privacy]: https://developer.apple.com/help/app-store-connect/manage-app-information/manage-app-privacy/
[a-guidelines]: https://developer.apple.com/app-store/review/guidelines/
[a-acctdel]: https://developer.apple.com/support/offering-account-deletion-in-your-app/
[a-export]: https://developer.apple.com/documentation/security/complying-with-encryption-export-regulations
[a-itsapp]: https://developer.apple.com/documentation/bundleresources/information-property-list/itsappusesnonexemptencryption
[a-asc-export]: https://developer.apple.com/help/app-store-connect/manage-app-information/overview-of-export-compliance
[a-encdoc]: https://developer.apple.com/help/app-store-connect/reference/app-information/export-compliance-documentation-for-encryption
[a-beta-export]: https://developer.apple.com/help/app-store-connect/test-a-beta-version/provide-export-compliance-information-for-beta-builds
[a-upcoming]: https://developer.apple.com/news/upcoming-requirements/
[a-notarize]: https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
[a-notary-issues]: https://developer.apple.com/documentation/security/resolving-common-notarization-issues
[a-agerating]: https://developer.apple.com/help/app-store-connect/reference/app-information/age-ratings-values-and-definitions
[ear-74017]: https://www.ecfr.gov/current/title-15/subtitle-B/chapter-VII/subchapter-C/part-740/section-740.17
[ms-manifest]: https://github.com/dotnet/macios/blob/main/docs/apple-privacy-manifest.md
[ms-manifest-learn]: https://learn.microsoft.com/en-us/dotnet/maui/ios/privacy-manifest
[ms-bcl]: https://github.com/dotnet/macios/blob/main/docs/required-reasons-bcl.md
[ms-maui-rr]: https://github.com/dotnet/macios/blob/main/docs/required-reasons-dotnet-maui.md
[maui-template-pm]: https://github.com/dotnet/maui/blob/main/src/Templates/src/templates/maui-mobile/Platforms/iOS/Resources/PrivacyInfo.xcprivacy
[maui-template-cat]: https://github.com/dotnet/maui/tree/main/src/Templates/src/templates/maui-mobile/Platforms/MacCatalyst
[maui-template-ent]: https://github.com/dotnet/maui/blob/main/src/Templates/src/templates/maui-mobile/Platforms/MacCatalyst/Entitlements.plist
[ms-publish]: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-app-store?view=net-maui-11.0
[rt-ssl]: https://github.com/dotnet/runtime/blob/release/11.0/src/libraries/System.Net.Security/src/System/Net/Security/SslStreamPal.OSX.cs
[maui-securestorage]: https://github.com/dotnet/maui/blob/main/src/Essentials/src/SecureStorage/SecureStorage.ios.tvos.watchos.macos.cs
[nuget-coreclr]: https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.maccatalyst-arm64/11.0.0
[nuget-ios]: https://www.nuget.org/packages/Microsoft.MacCatalyst.Runtime.maccatalyst-arm64.net11.0_27.2/27.2.11039
[nuget-maui-core]: https://www.nuget.org/packages/Microsoft.Maui.Core/11.0.10
[nuget-maui-ess]: https://www.nuget.org/packages/Microsoft.Maui.Essentials/11.0.10
[nuget-skia-cat]: https://www.nuget.org/packages/SkiaSharp.NativeAssets.MacCatalyst/4.157.0
[nuget-skia-mac]: https://www.nuget.org/packages/SkiaSharp.NativeAssets.macOS/4.157.0
[nuget-pdfium]: https://www.nuget.org/packages/bblanchon.PDFium.macOS/160.0.8179
[nuget-sqlite]: https://www.nuget.org/packages/SQLite/3.53.4
[vec0-release]: https://github.com/asg017/sqlite-vec/releases/tag/v0.1.9
