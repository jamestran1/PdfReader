# Pipeline phát hành Mac Catalyst với .NET 11: sign, notarize, App Store, CI (research cho #322)

Ngày: 2026-11-26. Ticket: #322, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Câu hỏi của ticket: từ `dotnet publish -f net11.0-maccatalyst` tới người dùng, mỗi kênh phát hành cần certificate, entitlement, lệnh và máy nào; một bản đã sandbox có dùng chung cho tải trực tiếp và Mac App Store không; CI trên runner macOS của GitHub Actions cần gì; khi Apple Developer Program hết hạn thì cái gì còn chạy; .NET 11 đòi mức macOS, kiến trúc và Xcode nào. Hạn: chủ repo phải chứng minh pipeline trước khi membership hết hạn vào cuối tháng 12/2026.

Cách đọc: **[Fact]** là điều tài liệu hoặc mã nguồn viết, có link ngay tại chỗ. **[Suy luận]** là nhận định của người nghiên cứu. "Chưa kiểm chứng" là chưa tìm được nguồn chính.

## Tóm tắt

- **Xcode 27.2 là điều kiện tiên quyết.** Workload .NET 11 cho Mac Catalyst (phát hành 10/11/2026) được build cho Xcode 27.2, và build báo lỗi E0191 nếu Xcode đang chọn khác major.minor. Runner `macos-latest` chỉ có Xcode tới 26.6, nên CI nên dùng image `xcode-27` và chọn Xcode 27.2. **[Fact]** cho các phiên bản và lỗi, **[Suy luận]** cho khuyến nghị chọn image. [dotnet/macios releases][gh-rel], [readme `xcode-27`][gh-xc27], [`_ValidateXcodeVersion`][src-sdktargets]
- **Tải trực tiếp:** sign bằng `Developer ID Application` với provisioning profile kiểu `Developer ID` (Mac Catalyst), `UseHardenedRuntime=true`, `.pkg` ký bằng `Developer ID Installer`, rồi `xcrun notarytool submit --wait` và `xcrun stapler staple`. **[Fact]** [Microsoft: publish outside the App Store][ms-outside], [Apple: customizing the notarization workflow][a-customize]
- **Mac App Store:** sign bằng `Apple Distribution` với profile `Mac App Store Connect`, `.pkg` ký bằng `Mac Installer Distribution` ("3rd Party Mac Developer Installer"), upload bằng `xcrun altool --upload-app` hoặc Transporter, thử qua TestFlight. **[Fact]** [Microsoft: publish for Mac App Store][ms-appstore], [Apple: upload builds][a-upload]
- **`dotnet publish` tự lo phần sign lồng nhau.** Khi publish cho macOS hoặc Mac Catalyst nó mặc định tạo `.pkg`, và SDK tự sign mọi `*.dylib` ở mọi thư mục con dưới `Contents/` (gồm PDFium, vec0) rồi mới sign bundle. Không cần `codesign` tay. **[Fact]** [Publish.targets][src-publish], [ComputeCodesignItems.cs][src-compute]
- **Entitlement:** `com.apple.security.app-sandbox` và `com.apple.security.network.client`. `com.apple.security.cs.allow-jit` được SDK tự thêm cho build Release. CoreCLR là runtime mặc định; `UseInterpreter` và `MtouchInterpreter` không có tác dụng. **[Fact]** [Xamarin.Shared.targets][src-shared], [Microsoft: interpreters][ms-interp]
- **Không cần `com.apple.security.cs.disable-library-validation`** khi dylib nằm trong bundle và do SDK sign cùng identity. **[Suy luận]** từ [Apple: Hardened Runtime][a-hardened] và [ComputeCodesignItems.cs][src-compute]
- **Hai kênh khác nhau ở bước sign và bước phát hành, không khác ở code.** Certificate, provisioning profile, khóa sign `.pkg` và `UseHardenedRuntime` đổi theo kênh; code, `Entitlements.plist`, `Info.plist`, cấu trúc bundle như nhau. Không tái dùng đúng một file `.app` đã sign: phải sign hai lần (publish hai lần từ cùng một commit). **[Fact]** cho từng khác biệt, **[Suy luận]** cho kết luận "hai lần publish"
- **Không có thuộc tính MSBuild "App Store" riêng.** Kênh được chọn bằng `CodesignKey`, `CodesignProvision`, `PackageSigningKey`. **[Fact]** [danh sách thuộc tính][ms-props]
- **Mức tối thiểu:** `SupportedOSPlatformVersion` 17.0 theo kiểu đánh số Mac Catalyst (macOS 14.0); template .NET 11 đặt 17.0; Microsoft ghi nhận hỗ trợ từ Mac Catalyst 18.2 (macOS 15.2). Phải đặt tường minh, nếu bỏ trống nó rơi về phiên bản SDK. **[Fact]** [pack GA][nuget-mac-sdk-pkg], [What's new][ms-whatsnew]
- **Kiến trúc:** mặc định Release là universal (`maccatalyst-x64;maccatalyst-arm64`) khi `SupportedOSPlatformVersion` dưới 27.0. Xuất arm64 bằng `-r maccatalyst-arm64`. **[Fact]** [Xamarin.Shared.Sdk.props][src-props], [Microsoft][ms-unsigned]
- **Khi membership hết hạn:** app Developer ID đã sign vẫn tải, cài và chạy được; bản ghi app trên App Store Connect vẫn còn, app rời Store và quay lại trong 24 giờ sau khi gia hạn, không phải nộp lại; không tạo được certificate và profile mới. Certificate Developer ID cấp từ G2 chỉ hiệu lực một năm. Notarization sau khi hết hạn: chưa kiểm chứng. **[Fact]** [Apple: Developer ID][a-devid], [Apple: renewal][a-renewal], [Apple: replace Developer ID certificates][a-replace]
- **Việc cần làm ngay, trước 31/12/2026:** tạo và sao lưu hai certificate Developer ID (chọn G2 Sub-CA), profile Developer ID, certificate App Store, profile Mac App Store Connect, App ID, bản ghi app và Team API key. Xem mục Checklist. **[Suy luận]** từ các Fact ở mục 5
- **Workaround `_SkipCodesignVerify` trong docs Microsoft đã cũ:** target `_CodesignVerify` không còn trong .NET 11. **[Fact]** [issue dotnet/macios #17829][gh-17829] (đóng 2023-03-21) và [mã nguồn GA][src-shared] không còn target này

## 0. Số phiên bản đã kiểm ngày 26/11/2026

| Thứ | Giá trị | Nguồn |
|---|---|---|
| Release dotnet/macios cho .NET 11 | `dotnet-11.0.1xx-xcode27.2.11039`, đăng 2026-11-10, ".NET 11 - Xcode 27.2 support" | [releases][gh-rel] |
| Workload manifest Mac Catalyst, band `11.0.100` | `Microsoft.NET.Sdk.MacCatalyst.Manifest-11.0.100` 27.2.11039, phiên bản duy nhất trên NuGet | [NuGet][nuget-mac-manifest] |
| Yêu cầu Xcode của manifest đó | `"xcode": {"version": "[27.2,)", "recommendedVersion": "27.2"}` | file `data/WorkloadDependencies.json` trong [gói manifest][nuget-mac-manifest-pkg] |
| SDK pack | `Microsoft.MacCatalyst.Sdk.net11.0_27.2` 27.2.11039; bên trong `RecommendedXcodeVersion` = 27.2 | [NuGet][nuget-mac-sdk] |
| Workload manifest MAUI, band `11.0.100` | `Microsoft.NET.Sdk.Maui.Manifest-11.0.100` 11.0.0 | [NuGet][nuget-maui-manifest] |
| Thư viện MAUI | `Microsoft.Maui.Controls` 11.0.0 và 11.0.10 (ổn định) | [NuGet][nuget-maui-controls] |
| Runtime CoreCLR cho Mac Catalyst arm64 | `Microsoft.NETCore.App.Runtime.Maccatalyst.arm64` 11.0.0 | [NuGet][nuget-coreclr-cat] |
| Runner `macos-latest` | macOS 26.7.2 arm64; Xcode 26.0.1 tới 26.6 (mặc định 26.6); .NET SDK có 11.0.100 | [readme][gh-mac26] |
| Runner `xcode-27` | macOS 27.0.1 arm64; Xcode 27.0, 27.1 (mặc định), 27.2; .NET SDK chỉ tới 10.0.403 | [readme][gh-xc27] |
| Xcode 27.2 | cần macOS Tahoe 26.6 trở lên để chạy | [Apple: Xcode][a-xcode] |

Tôi đọc các file ở trên trực tiếp từ gói NuGet (manifest và pack `Microsoft.MacCatalyst.Sdk.net11.0_27.2`), không suy từ trí nhớ.

## 1. Tải trực tiếp: Developer ID, Hardened Runtime, dylib, `.pkg`, `notarytool`, staple

**Máy nào.** `notarytool` và `stapler` đi kèm Xcode; docs Microsoft chỉ hướng dẫn publish Mac Catalyst "from the command line on a Mac". Mọi bước sign, đóng gói, notarize vì vậy chạy trên macOS có Xcode 27.2: Mac mini của chủ repo hoặc runner `xcode-27`. **[Fact]** [Apple][a-notarize], [Microsoft][ms-outside]. Sign bằng Developer ID cần Internet để lấy secure timestamp từ `timestamp.apple.com`. **[Fact]** [TN3161][a-tn3161]

**Chuẩn bị một lần** (vai trò Account Holder, trên developer.apple.com/account):

1. Tạo CSR trong Keychain Access; hai certificate cần hai CSR khác nhau. **[Fact]** [Microsoft][ms-outside]
2. Certificate `Developer ID Application` (sign app) và `Developer ID Installer` (sign `.pkg`): khi được hỏi intermediary, chọn **G2 Sub-CA**. Certificate do CA cũ cấp không dùng để sign được từ 01/02/2027; certificate G2 hiệu lực một năm và phải gia hạn hằng năm. **[Fact]** [Apple][a-replace]
3. App ID kiểu explicit, bundle ID trùng `ApplicationId` của project. **[Fact]** [Microsoft][ms-outside]
4. Provisioning profile kiểu `Developer ID`, ở bước tiếp theo chọn `Mac Catalyst`, gắn với App ID và certificate `Developer ID Application`. **[Fact]** [Microsoft][ms-outside]. Profile Developer ID tạo sau 22/02/2017 hiệu lực 18 năm, không phụ thuộc hạn của certificate. **[Fact]** [Apple][a-devid]
5. File `Platforms/MacCatalyst/Entitlements.plist`.

**Lệnh, trên macOS:**

```sh
dotnet publish -f net11.0-maccatalyst -c Release -r maccatalyst-arm64 \
  -p:UseHardenedRuntime=true \
  -p:EnableCodeSigning=true \
  -p:CodesignKey="Developer ID Application: <Tên> (<TEAMID>)" \
  -p:CodesignProvision="<tên profile Developer ID>" \
  -p:CodesignEntitlements="Platforms/MacCatalyst/Entitlements.plist" \
  -p:CreatePackage=true -p:EnablePackageSigning=true \
  -p:PackageSigningKey="Developer ID Installer: <Tên> (<TEAMID>)"
```

- Đây là lệnh của docs Microsoft cộng `-r maccatalyst-arm64` và `UseHardenedRuntime`, viết lại cho `net11.0-maccatalyst`. **[Fact]** [Microsoft][ms-outside]
- `UseHardenedRuntime=true` bật hai thứ cùng lúc: cờ `runtime` và `--timestamp`. Không bật thì task `Codesign` truyền `--timestamp=none`, và notarization fail với "The signature does not include a secure timestamp". **[Fact]** [Xamarin.Shared.targets][src-shared], [Codesign.cs][src-codesign], [Apple][a-resolve]
- Notarization bắt buộc Hardened Runtime. **[Fact]** [Apple][a-hardened]
- Khi publish cho macOS hoặc Mac Catalyst, `CreatePackage` mặc định là `true` và `.pkg` nằm trong thư mục `publish/`. **[Fact]** [Publish.targets][src-publish]. Publish một kiến trúc thì output có thêm thư mục kiến trúc. **[Fact]** [Microsoft][ms-outside]
- Không thêm target `_SkipCodesignVerify` như docs Microsoft còn ghi: `_CodesignVerify` không còn trong mã nguồn GA. **[Fact]** [dotnet/macios #17829][gh-17829], [Xamarin.Shared.targets][src-shared]

**Notarize và staple:**

Tên file là `<AssemblyName>-<CFBundleShortVersionString>.pkg`. **[Fact]** [CreateInstallerPackage.cs][src-installer]

```sh
xcrun notarytool submit TriThu-1.0.0.pkg --key AuthKey_<ID>.p8 --key-id <ID> --issuer <UUID> --wait
xcrun notarytool log <submission-id> --key AuthKey_<ID>.p8 --key-id <ID> --issuer <UUID>   # khi cần xem log
xcrun stapler staple TriThu-1.0.0.pkg
xcrun stapler validate TriThu-1.0.0.pkg
pkgutil --check-signature TriThu-1.0.0.pkg
```

- Notary service nhận disk image (UDIF), flat installer package đã sign và ZIP; không nhận `.app` trực tiếp. Nó cấp ticket cho file ngoài cùng và từng file lồng bên trong. **[Fact]** [Apple][a-customize]
- `notarytool` nhận ba kiểu credential: mật khẩu app-specific (`--apple-id`, `--password`, `--team-id`), App Store Connect API key (`--issuer`, `--key-id`, `--key`), hoặc keychain profile tạo bằng `xcrun notarytool store-credentials`. **[Fact]** [Apple][a-customize], [TN3147][a-tn3147]
- `altool` không còn được notary service nhận từ 01/11/2023. **[Fact]** [Apple][a-notarize]
- Hầu hết notarization xong trong 5 phút, 98% trong 15 phút; giới hạn 75 lượt mỗi ngày. **[Fact]** [Apple][a-customize]
- Mọi phần mềm phân phối bằng Developer ID phải notarize; phần mềm qua Mac App Store thì không phải. **[Fact]** [Apple][a-notarize]

**SDK đã làm gì với dylib và entitlement:**

- Task `ComputeCodesignItems` tìm mọi `*.dylib` ở mọi thư mục con dưới `Contents/`, `*.framework` trong `Contents/Frameworks`, và `*.xpc`; tất cả được sign bằng cùng khóa với bundle, không kèm entitlement của app; sau đó mới sign bundle. **[Fact]** [ComputeCodesignItems.cs][src-compute]
- Apple: entitlement chỉ đặt trên executable; thư viện và framework dùng lại entitlement của tiến trình chủ. Apple cũng khuyên không tắt library validation với app không chứa plug-in. **[Fact]** [Apple][a-hardened], [Apple][a-resolve]
- Từ hai Fact trên: **[Suy luận]** `disable-library-validation` không cần cho PDFium và vec0, với điều kiện chúng nằm trong bundle trước khi sign.
- SDK tự thêm `com.apple.security.cs.allow-jit` cho build Release trên macOS và Mac Catalyst. **[Fact]** [Xamarin.Shared.targets][src-shared]
- Ở build Debug có sandbox, SDK tự thêm `com.apple.security.network.client` cho debugger; ở Release phải tự thêm (app gọi API AI). **[Fact]** [CompileEntitlements.cs][src-ent]
- **[Suy luận], cần kiểm trên Mac:** dylib phải có slice arm64 và platform `MacCatalyst` (hoặc "zippered" cho cả macOS và Mac Catalyst); dylib build thuần cho macOS có thể không nạp được vào tiến trình Mac Catalyst. Chưa tìm được tài liệu Apple để trích.

**`.pkg` hay `.dmg`.** `dotnet publish` tạo `.pkg` bằng `productbuild --component <app> /Applications --sign "<tên certificate>"`. **[Fact]** [CreateInstallerPackage.cs][src-installer]. `productbuild` với tùy chọn sign có secure timestamp mặc định, và `.pkg` có trusted timestamp vẫn cài được sau khi certificate hết hạn. **[Fact]** [Apple][a-resolve], [Apple: certificates overview][a-certs]. SDK không có thuộc tính nào tạo `.dmg`. **[Fact]** [danh sách thuộc tính][ms-props]

Nguồn: [Apple: Notarizing macOS software before distribution][a-notarize], [Apple: Customizing the notarization workflow][a-customize], [Apple: Resolving common notarization issues][a-resolve], [Apple: Hardened Runtime][a-hardened], [TN3161][a-tn3161], [Microsoft: publish outside the App Store][ms-outside], [Xamarin.Shared.targets][src-shared], [ComputeCodesignItems.cs][src-compute], [Codesign.cs][src-codesign]

## 2. Mac App Store: certificate, profile, upload, TestFlight

**Bundle ID và bản ghi app:**

1. App ID explicit (mục 1, bước 3). Trong cấu hình App ID, bật capability `Mac Catalyst`, chọn "Use existing Mac App ID" và chỉ vào chính App ID đó khi app là sản phẩm riêng. **[Fact]** [Microsoft][ms-appstore]
2. Trong App Store Connect: Apps, nút `+`, New App, chọn platform macOS và nhập thông tin app. Bản ghi mới ở trạng thái "Prepare for Submission". Không tạo được app khi Account Holder chưa ký thỏa thuận mới nhất trong mục Business. **[Fact]** [Apple][a-add-app]
3. Mỗi lần upload, bundle ID và version trong bundle được dùng để gắn build với bản ghi app. **[Fact]** [Apple][a-upload]

**Certificate và profile** (mỗi cái một CSR): docs Microsoft dùng certificate `Apple Distribution` để sign app; trang Apple còn liệt kê `Mac App Distribution` với cùng mục đích "Sign a Mac app before submitting it to the Mac App Store". Certificate `Mac Installer Distribution`, trong Keychain có tên "3rd Party Mac Developer Installer", dùng để sign `.pkg`. **[Fact]** [Microsoft][ms-appstore], [Apple: certificates overview][a-certs]

Profile: Profiles, `+`, chọn "Mac App Store Connect" (dành cho app macOS, kể cả Mac Catalyst), chọn App ID rồi certificate distribution; một profile chỉ chứa một certificate distribution. **[Fact]** [Apple][a-asc-profile]

**Info.plist,** dùng chung cho cả hai kênh: `LSApplicationCategoryType` (trùng category chính trên App Store Connect), `ITSAppUsesNonExemptEncryption`, `CFBundleDevelopmentRegion`, `UIDeviceFamily`. **[Fact]** [Microsoft][ms-appstore]

**Lệnh:** cùng lệnh ở mục 1, đổi ba giá trị (ví dụ của docs Microsoft, viết lại cho `net11.0-maccatalyst`): **[Fact]** [Microsoft][ms-appstore]

```sh
-p:CodesignKey="Apple Distribution: <Tên> (<TEAMID>)" \
-p:CodesignProvision="<tên profile Mac App Store Connect>" \
-p:PackageSigningKey="3rd Party Mac Developer Installer: <Tên> (<TEAMID>)"
```

App đã sign bằng certificate distribution không chạy được tại máy; nó được Apple sign lại sau khi upload. **[Fact]** [Microsoft][ms-appstore], [TN3161][a-tn3161]

**Upload, chọn một trong ba:**

- `xcrun altool --validate-app -f <file> -t <platform> -u <username> [-p <password>]` rồi `xcrun altool --upload-app -f <file> -t <platform> -u <username> [-p <password>]`; upload dùng được cho mọi loại target với Transporter và altool. **[Fact]** [Apple][a-upload]. Với API key, `altool` dùng `--apiKey` và `--apiIssuer`, và tìm file `.p8` theo key ID (ví dụ `~/.appstoreconnect/private_keys/AuthKey_<ID>.p8`). **[Fact]** [TN3147][a-tn3147]. Giá trị `<platform>` cho macOS (thường ghi `osx`): chưa kiểm chứng, xem `xcrun altool --help` trên Mac.
- Transporter (app trên Mac App Store), hoặc công cụ dòng lệnh của Transporter với JWT. **[Fact]** [Apple][a-upload]
- `xcodebuild -exportArchive -archivePath <x.xcarchive> -exportOptionsPlist ExportOptions.plist -exportPath <thư mục>`. Cần `.xcarchive`: `dotnet publish -p:ArchiveOnBuild=true` (mặc định vào `~/Library/Developer/Xcode/Archives`, đổi bằng `ArchiveDir`). **[Fact]** [danh sách thuộc tính][ms-props]. Apple khuyên lấy `ExportOptions.plist` từ một lần export thủ công trong Xcode. **[Fact]** [Apple][a-customize]. Khóa của `ExportOptions.plist` cho Mac Catalyst: chưa kiểm chứng, xem `xcodebuild -help`.

Yêu cầu upload: từ 28/04/2026, app tải lên App Store Connect phải build bằng Xcode 26 trở lên với SDK cho iOS 26, iPadOS 26, tvOS 26, visionOS 26 hoặc watchOS 26 (macOS không được liệt kê riêng). Build bằng Xcode 27.2 thỏa. **[Fact]** [Apple: upcoming requirements][a-upcoming]

**TestFlight cho macOS:** build macOS phải build bằng Xcode 13 trở lên; build đủ điều kiện phải có application identifier trong provisioning profile; tối đa 100 internal tester và 10.000 external tester; build dùng thử được 90 ngày. Tester cài app TestFlight trên Mac. **[Fact]** [Apple: TestFlight Overview][a-tf-overview], [Apple: add internal testers][a-tf-internal]. Quy trình từng bước cho app .NET (tạo bản ghi, group internal, thêm build): [dotnet/macios: Preparing your App for TestFlight][gh-tf], viết cho .NET 7 nhưng các bước App Store Connect không đổi.

Nguồn: [Microsoft: publish for Mac App Store distribution][ms-appstore], [Apple: Create an App Store Connect provisioning profile][a-asc-profile], [Apple: Add a new app][a-add-app], [Apple: Upload builds][a-upload], [Apple: TestFlight Overview][a-tf-overview], [TN3147][a-tn3147], [TN3161][a-tn3161]

## 3. Một bản đã sandbox có dùng cho cả hai kênh không?

Có, theo nghĩa một mã nguồn, một `Entitlements.plist`, một cấu trúc bundle. Không, theo nghĩa một file `.app` đã sign: chữ ký khác nhau và không kênh nào nhận chữ ký của kênh kia.

| Khía cạnh | Tải trực tiếp | Mac App Store | Khác ở đâu |
|---|---|---|---|
| Certificate sign `.app` | `Developer ID Application` | `Apple Distribution` | bước sign |
| Certificate sign `.pkg` | `Developer ID Installer` | `Mac Installer Distribution` | bước sign |
| Provisioning profile | `Developer ID` (Mac Catalyst) | `Mac App Store Connect` | bước sign |
| Hardened Runtime | bắt buộc để notarize | Apple chỉ nêu yêu cầu này cho notarization | bước sign (`UseHardenedRuntime`) |
| App Sandbox | tùy chọn | bắt buộc | như nhau (quyết định của map là bật) |
| `allow-jit` | SDK tự thêm ở Release | SDK tự thêm ở Release | như nhau |
| `Info.plist` | như mục 2 | như mục 2 | như nhau |
| Sau sign | notarize, staple | upload, Apple sign lại | bước phát hành |

- Notary service chỉ nhận phần mềm sign bằng Developer ID; certificate `Mac App Distribution` bị từ chối. **[Fact]** [Apple][a-resolve]
- App Sandbox "can optionally be enabled" cho Mac Catalyst ngoài App Store và "must be enabled" trên Mac App Store; nguyên tắc 2.4.5(i) của App Review Guidelines: app trên Mac App Store "must be appropriately sandboxed". **[Fact]** [Microsoft][ms-outside], [Apple][a-review]
- Capability App Sandbox và Hardened Runtime đều có cho Apple Developer Program và Developer ID. **[Fact]** [Apple: supported capabilities (macOS)][a-caps]
- **[Suy luận]** Cách làm: publish hai lần từ cùng một commit, đổi `CodesignKey`, `CodesignProvision`, `PackageSigningKey`. Giữ `UseHardenedRuntime=true` cho cả hai để giảm khác biệt.
- Entitlement có thể giống nhau, nhưng phía App Store entitlement còn bị đối chiếu với capability của App ID và của profile; `ValidateEntitlements` của SDK mặc định báo lỗi. **[Fact]** [Microsoft][ms-appstore], [danh sách thuộc tính][ms-props]
- **Rủi ro, thuộc ticket research App Sandbox:** hai profile gắn với cùng một App ID nên cùng bundle ID là khả thi, nhưng hai bản sign khác nhau cùng bundle ID và cùng sandbox thì dùng chung container dữ liệu hay không, tôi chưa kiểm chứng. **[Suy luận]** Việc cài cả hai bản cùng bundle ID trên một Mac cũng dễ ghi đè lẫn nhau.

Nguồn: [Apple: Resolving common notarization issues][a-resolve], [Apple: Hardened Runtime][a-hardened], [Microsoft: publish outside the App Store][ms-outside], [Microsoft: publish for Mac App Store distribution][ms-appstore], [Apple: App Review Guidelines][a-review]

## 4. CI trên GitHub Actions runner macOS

**Image.** Bảng "Available Images" của `actions/runner-images` hiện ghi: `macos-latest` (cùng `macos-26`, `macos-26-xlarge`) là macOS 26 Arm64; `xcode-27` (cùng `xcode-27-xlarge`) là image chạy trên macOS 27. **[Fact]** [readme][gh-readme], [issue #14404][gh-14404]

- `macos-latest` có sẵn .NET SDK 11.0.100 nhưng Xcode chỉ tới 26.6, không đủ cho workload .NET 11 (cần 27.2). **[Fact]** [readme][gh-mac26]
- `xcode-27` có Xcode 27.0, 27.1 (mặc định) và 27.2, nhưng .NET SDK chỉ tới 10.0.403. Phải cài .NET 11 và phải chuyển sang Xcode 27.2 trước khi build. **[Fact]** [readme][gh-xc27]
- Issue công bố image ghi nó "marked as preview" từ 07/2026, từ 16/09/2026 đổi sang nền macOS 27, và đã đóng. **[Fact]** [issue #14404][gh-14404]

**Workload .NET 11.** Trên macOS, docs Microsoft ghi `sudo dotnet workload install maui`; phiên bản Xcode phải khớp phiên bản MAUI. **[Fact]** [Microsoft: installation][ms-install]. `actions/setup-dotnet@v6` có input `workloads`, tự chạy `dotnet workload update` trước khi cài. **[Fact]** [setup-dotnet][gh-setupdotnet]

- Cài được hay không: các gói manifest cho band `11.0.100` có trên NuGet. **[Fact]** [NuGet][nuget-mac-manifest], [NuGet][nuget-maui-manifest]. Tôi không có runner để chạy thử, nên "cài được trên runner `xcode-27` sau khi cài .NET 11 SDK" là **[Suy luận]**.
- `maui` là `maui-mobile` cộng `maui-desktop`, kéo theo workload `android`, `ios`, `maccatalyst`. Chỉ cần Mac thì có workload hẹp hơn `maui-maccatalyst`, với điều kiện project không restore các target framework khác. **[Fact]** về thành phần manifest [NuGet][nuget-maui-manifest]; **[Suy luận]** về việc dùng workload hẹp. Template .NET 11 chỉ thêm target `windows` khi build trên Windows, nên `maui` đầy đủ vẫn là lựa chọn an toàn. **[Fact]** về template [Microsoft.Maui.Templates.net11][nuget-templates]; **[Suy luận]** về khuyến nghị.
- Nhãn `-latest` trỏ tới image GA mới nhất và được dời dần, nên ghi nhãn cụ thể (`xcode-27`) trong workflow. **[Fact]** về quy tắc nhãn [readme][gh-readme]; **[Suy luận]** về khuyến nghị.
- Dung lượng nén tôi đo trên NuGet ngày 26/11/2026 (kích thước file `.nupkg`, ví dụ [pack Mac Catalyst][nuget-mac-sdk-pkg]): các pack của workload `maccatalyst` khoảng 74 MB; các pack MAUI core và Blazor khoảng 85 MB; runtime Mono Mac Catalyst arm64 và x64 khoảng 60 MB; riêng `Microsoft.iOS.Sdk.net11.0_27.2` 72.7 MB.
- Thời gian cài workload và thời gian build: chưa kiểm chứng, không có tài liệu nào ghi. **[Suy luận]** 5 đến 15 phút cho cài workload, 3 đến 8 phút cho restore cộng publish Release arm64.
- Ghim SDK bằng `global.json`: manifest gắn với band `11.0.100`, band khác là gói manifest khác. Không ghim thì runner dùng .NET SDK mới nhất đã cài. **[Fact]** [setup-dotnet][gh-setupdotnet]; **[Suy luận]** cho khuyến nghị ghim.

**Khung workflow** (lệnh tạo keychain chưa chạy thử trên runner):

```yaml
jobs:
  mac-release:
    runs-on: xcode-27
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '11.0.x'
          workloads: maui
      - run: sudo xcode-select -s /Applications/Xcode_27.2.app
      - run: dotnet publish <project> -f net11.0-maccatalyst -c Release -r maccatalyst-arm64 <tham số ở mục 1>
      - run: xcrun notarytool submit <pkg> --key <p8> --key-id "$ASC_KEY_ID" --issuer "$ASC_ISSUER_ID" --wait
      - run: xcrun stapler staple <pkg>
```

Trên runner, import `.p12` vào một keychain tạm rồi truyền `-p:CodesignKeychain=<đường dẫn>` (thuộc tính có trong SDK). **[Fact]** về thuộc tính [danh sách thuộc tính][ms-props]; **chưa kiểm chứng** về chuỗi lệnh `security create-keychain`, `security import`, `security set-key-partition-list`.

Nguồn: [actions/runner-images][gh-readme], [readme macOS 26 arm64][gh-mac26], [readme `xcode-27`][gh-xc27], [actions/setup-dotnet][gh-setupdotnet], [Microsoft: installation][ms-install], [NuGet manifest MAUI][nuget-maui-manifest]

## 5. Khi Apple Developer Program hết hạn

| Câu hỏi | Trả lời | Mức |
|---|---|---|
| Certificate Developer ID còn dùng được không? | Apple: "If your membership expires, users can still download, install, and run your applications that are signed with Developer ID. However, once your Developer ID certificate expires, you must be an Apple Developer Program member to get new Developer ID certificates". Certificate G2 chỉ hiệu lực một năm, nên tối đa một năm sau khi tạo là hết đường sign bản mới nếu không gia hạn membership. | **[Fact]** [Apple][a-devid], [Apple][a-replace] |
| Có sign được bản mới bằng certificate còn hạn? | Apple không viết thẳng; câu ở trên ngụ ý certificate còn dùng tới khi hết hạn. Khi hết hạn membership thì mất quyền truy cập "Certificates, Identifiers & Profiles". | **[Suy luận]** từ [Apple][a-devid], [Apple][a-renewal] |
| Notarization có cần membership còn hạn không? | Chưa kiểm chứng. `notarytool` đăng nhập bằng tài khoản Apple hoặc Team key của App Store Connect; Individual key không dùng được với `notarytool`. | chưa kiểm chứng; **[Fact]** về key [Apple][a-apikey] |
| Bản đã notarize còn chạy không? | Có. Gatekeeper kiểm tra certificate Developer ID còn hiệu lực tại thời điểm sign; app đã notarize giữ nguyên. Certificate bị thu hồi thì app không cài và không chạy được nữa. | **[Fact]** [Apple][a-devid], [TN3161][a-tn3161] |
| File `.pkg` còn cài được không? | Có, khi có trusted timestamp. Trang Developer ID của Apple viết ".pkg" chỉ chạy khi certificate Developer ID Installer còn hạn; trang Certificates overview ghi có trusted timestamp thì vẫn cài được sau khi certificate hết hạn. Hai trang không thống nhất với nhau. | **[Fact]** kèm mâu thuẫn [Apple: overview][a-certs], [Apple][a-devid] |
| Profile Developer ID còn dùng được không? | Có, 18 năm; app dùng profile mà profile hết hạn thì không khởi chạy được. | **[Fact]** [Apple][a-devid] |
| Bản ghi app trên App Store Connect còn không? | Còn. Khi hết hạn: app không còn tải được, không nộp được app hay bản cập nhật mới. Gia hạn sau đó: app miễn phí hiện lại trong 24 giờ, "You don't need to resubmit your apps". | **[Fact]** [Apple][a-renewal] |
| TestFlight còn chạy không? | Chưa kiểm chứng. | chưa kiểm chứng |

Gia hạn bằng tay được từ 30 ngày trước ngày hết hạn, hoặc bất kỳ lúc nào sau khi hết hạn; gia hạn sớm thì có hiệu lực khi membership cũ hết. **[Fact]** [Apple][a-renewal]

Hai điểm cần chú ý trước ngày hết hạn:

- Không thu hồi certificate Developer ID đã dùng để sign bản phát hành: app sign bằng certificate bị thu hồi không cài và không chạy được. **[Fact]** [Apple][a-devid]
- CA Developer ID gốc hết hạn 01/02/2027. Certificate cũ cấp từ CA này không sign được nữa, và `.pkg` sign bằng nó không cài được từ ngày đó. Nếu chủ repo đã có certificate Developer ID cũ thì tạo cái mới từ G2 Sub-CA. **[Fact]** [Apple][a-replace]

Nguồn: [Apple: Developer ID certificates][a-devid], [Apple: Program Renewal][a-renewal], [Apple: Replacing Developer ID certificates][a-replace], [Apple: Certificates overview][a-certs], [TN3161][a-tn3161]

## 6. .NET 11 với Mac Catalyst: mức macOS, `SupportedOSPlatformVersion`, arm64, Xcode

- **Mức tối thiểu của SDK:** `MacCatalystMinSupportedOSPlatformVersion` là 17.0 trong pack GA. Thấp hơn sẽ bị lỗi E7126 "The SupportedOSPlatformVersion value ... is lower than the minimum value". Theo bảng `MacCatalystVersionMap`, 17.0 (số theo kiểu iOS của Mac Catalyst) là macOS 14.0. **[Fact]** [pack GA][nuget-mac-sdk-pkg] (file `targets/Microsoft.MacCatalyst.Sdk.SupportedTargetPlatforms.props`), [CompileAppManifest.cs][src-manifest], [Versions-MacCatalyst.plist.in][src-plist]
- **Mức Microsoft ghi là hỗ trợ:** ".NET 11 on iOS, tvOS, Mac Catalyst, and macOS supports the following platform versions: ... Mac Catalyst: 18.2 ... macOS: 15.2". Release notes .NET 11 liệt kê macOS 27, 26, 15. **[Fact]** [What's new in .NET MAUI for .NET 11][ms-whatsnew], [supported-os][core-os]
- **[Suy luận]:** với bản macOS, đặt `SupportedOSPlatformVersion` 18.2 (macOS 15.2) để nằm trong mức Microsoft hỗ trợ.
- **Phải đặt tường minh:** khi không đặt, giá trị lấy `EffectiveTargetPlatformVersion`, tức `TargetPlatformVersion`; project executable dùng `TargetPlatformVersion` mới nhất, với `net11.0-maccatalyst` không kèm số là 27.2, nghĩa là app chỉ chạy trên macOS 27.2 trở lên. **[Fact]** về cơ chế [Microsoft.NET.Sdk.targets][sdk-targets], [CompileAppManifest.cs][src-manifest], [danh sách thuộc tính][ms-props]. Template .NET 11 của MAUI đặt 17.0 cho Mac Catalyst. **[Fact]** [Microsoft.Maui.Templates.net11][nuget-templates]
- **Kiến trúc mặc định:** build Release với `SupportedOSPlatformVersion` dưới 27.0, khi không có `RuntimeIdentifier` lẫn `RuntimeIdentifiers`, mặc định là universal `maccatalyst-x64;maccatalyst-arm64`. Từ 27.0 trở lên, hoặc ở build Debug, mặc định là kiến trúc của máy build (`maccatalyst-arm64` trên Apple Silicon). **[Fact]** [Xamarin.Shared.Sdk.props][src-props], [Microsoft][ms-outside]
- **Xuất arm64:** thêm `-r maccatalyst-arm64` (hoặc `-p:RuntimeIdentifier=maccatalyst-arm64`). **[Fact]** [Microsoft][ms-outside]
- **Xcode:** 27.2 là phiên bản được khuyến nghị. `_ValidateXcodeVersion` so major.minor của Xcode đang chọn với `RecommendedXcodeVersion`, lệch thì báo E0191; `ValidateXcodeVersion=false` tắt được nhưng không nên. **[Fact]** [`_ValidateXcodeVersion`][src-sdktargets], [danh sách thuộc tính][ms-props]
- **Runtime:** CoreCLR là runtime mặc định. Build Release dùng composite full ReadyToRun; interpreter của CoreCLR luôn bật cho phần chưa biên dịch trước. **[Fact]** [Microsoft: interpreters][ms-interp]

Nguồn: [pack GA trên NuGet][nuget-mac-sdk-pkg], [Xamarin.Shared.Sdk.props][src-props], [Xamarin.Shared.Sdk.targets][src-sdktargets], [Microsoft: What's new in .NET 11][ms-whatsnew], [Microsoft: interpreters][ms-interp], [dotnet/core: supported OS 11.0][core-os], [dotnet/sdk: Microsoft.NET.Sdk.targets][sdk-targets]

## Checklist certificate và secret

Làm các dòng có "Tạo" khi membership còn hạn. Vai trò cần thiết: Account Holder cho certificate Developer ID và cho việc tạo Team key.

| Thứ | Loại | Kênh | Tạo ở đâu | Secret cho CI |
|---|---|---|---|---|
| Certificate `Developer ID Application` | sign `.app` | tải trực tiếp | Certificates, `+`, Developer ID Application, chọn G2 Sub-CA | `DEV_ID_APP_P12` (base64), `P12_PASSWORD` |
| Certificate `Developer ID Installer` | sign `.pkg` | tải trực tiếp | Certificates, `+`, Developer ID Installer, chọn G2 Sub-CA | gộp chung một file `.p12` với dòng trên, hoặc `DEV_ID_INSTALLER_P12` |
| Profile `Developer ID` (Mac Catalyst) | provisioning | tải trực tiếp | Profiles, `+`, Developer ID, chọn Mac Catalyst | `DEV_ID_PROVISION` (base64 của file `.provisionprofile`) |
| Certificate `Apple Distribution` | sign `.app` | App Store, TestFlight | Certificates, `+`, Apple Distribution | `ASC_DIST_P12`, mật khẩu |
| Certificate `Mac Installer Distribution` | sign `.pkg` | App Store | Certificates, `+`, Mac Installer Distribution | gộp với dòng trên |
| Profile `Mac App Store Connect` | provisioning | App Store, TestFlight | Profiles, `+`, Mac App Store Connect, chọn Mac | `ASC_PROVISION` (base64) |
| App ID explicit, bật `Mac Catalyst` | identifier | cả hai | Identifiers, `+`, App IDs | `APPLE_TEAM_ID` |
| Bản ghi app, platform macOS | App Store Connect | App Store, TestFlight | Apps, `+`, New App | không |
| Team API key (file `.p8`) | xác thực | notarization, upload | Users and Access, Integrations, Team Keys; tải `.p8` được một lần | `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` |
| Mật khẩu app-specific | xác thực | chỉ notarization | tài khoản Apple, thay cho API key | `APPLE_ID`, `APPLE_APP_PASSWORD` |

- Tạo certificate Developer ID: tối đa năm cái mỗi loại; vai trò Account Holder. **[Fact]** [Apple][a-devid]
- Tạo Team key: cần tài khoản Admin trong App Store Connect; Individual key không dùng được với `notarytool`. **[Fact]** [Apple][a-apikey]. Vai trò tối thiểu của key để upload build: chưa kiểm chứng.
- CSR: Keychain Access, Certificate Assistant; để trống CA Email Address, chọn "Saved to disk". **[Fact]** [Apple][a-csr]

Thứ tự làm trên Mac mini, mỗi bước chỉ sang bước sau khi bước trước chạy được:

**[Suy luận]** thứ tự đề xuất; các lệnh kiểm tra là lệnh quen dùng, tôi chưa chạy thử trên Mac.

1. Kiểm tra môi trường: `xcode-select -p` trỏ Xcode 27.2, `dotnet --version` thuộc band 11.0.100, `dotnet workload list` có `maui`.
2. Tạo App ID, hai certificate Developer ID, profile Developer ID. Ghi lại tên đầy đủ của certificate bằng `security find-identity -v -p codesign`.
3. Publish bản tải trực tiếp theo lệnh ở mục 1, kiểm bằng `codesign -dvv <app>` (kỳ vọng `Authority=Developer ID Application`, `Timestamp=` theo [TN3161][a-tn3161], và `flags=0x10000(runtime)`).
4. `xcrun notarytool store-credentials` rồi submit, staple, cài thử file `.pkg`.
5. Tạo bản ghi app, hai certificate App Store, profile Mac App Store Connect, Team key.
6. Publish bản App Store theo lệnh ở mục 2, `xcrun altool --validate-app`, rồi `--upload-app`; thấy build trong tab TestFlight là pipeline được chứng minh.
7. Xuất `.p12` (có mật khẩu), file `.provisionprofile`, file `.p8`; lưu ngoài repo; đưa lên GitHub secret cho CI.

Nguồn: [Apple: Developer ID certificates][a-devid], [Apple: Create an App Store Connect provisioning profile][a-asc-profile], [Apple: Creating API keys][a-apikey], [Apple: Create a certificate signing request][a-csr], [TN3161][a-tn3161]

## Chưa kiểm chứng

- Thời lượng cài workload và build trên runner `xcode-27`; chuỗi lệnh `security` để import `.p12` trên runner.
- Giá trị `<platform>` của `xcrun altool` cho macOS; khóa của `ExportOptions.plist` cho Mac Catalyst; thư mục SDK tìm file `.provisionprofile` theo tên `CodesignProvision`.
- Notarization sau khi membership hết hạn; TestFlight sau khi hết hạn.
- Hai bản sign khác nhau, cùng bundle ID, cùng sandbox: có dùng chung container dữ liệu không (thuộc ticket research App Sandbox).
- Dylib PDFium và vec0 cần platform `MacCatalyst` hay "zippered" để nạp được vào tiến trình Mac Catalyst.
- App có gửi nội dung tài liệu tới dịch vụ AI thì cần khai báo privacy manifest và export compliance khi review; chưa nghiên cứu trong ticket này.

[a-notarize]: https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution
[a-customize]: https://developer.apple.com/documentation/security/customizing-the-notarization-workflow
[a-resolve]: https://developer.apple.com/documentation/security/resolving-common-notarization-issues
[a-hardened]: https://developer.apple.com/documentation/security/hardened-runtime
[a-tn3147]: https://developer.apple.com/documentation/technotes/tn3147-migrating-to-the-latest-notarization-tool
[a-tn3161]: https://developer.apple.com/documentation/technotes/tn3161-inside-code-signing-certificates
[a-devid]: https://developer.apple.com/help/account/certificates/create-developer-id-certificates/
[a-replace]: https://developer.apple.com/help/account/certificates/replace-developer-id-certificates/
[a-certs]: https://developer.apple.com/help/account/certificates/certificates-overview/
[a-renewal]: https://developer.apple.com/help/account/membership/renewal/
[a-asc-profile]: https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile/
[a-add-app]: https://developer.apple.com/help/app-store-connect/create-an-app-record/add-a-new-app/
[a-upload]: https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/
[a-tf-overview]: https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/
[a-tf-internal]: https://developer.apple.com/help/app-store-connect/test-a-beta-version/add-internal-testers/
[a-apikey]: https://developer.apple.com/documentation/appstoreconnectapi/creating-api-keys-for-app-store-connect-api
[a-xcode]: https://developer.apple.com/support/xcode/
[a-upcoming]: https://developer.apple.com/news/upcoming-requirements/
[a-review]: https://developer.apple.com/app-store/review/guidelines/
[a-caps]: https://developer.apple.com/help/account/reference/supported-capabilities-macos/
[a-csr]: https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request/
[ms-outside]: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-outside-app-store?view=net-maui-11.0
[ms-appstore]: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-app-store?view=net-maui-11.0
[ms-unsigned]: https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/publish-unsigned?view=net-maui-11.0
[ms-interp]: https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter?view=net-maui-11.0
[ms-whatsnew]: https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11?view=net-maui-11.0
[ms-install]: https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-11.0
[ms-props]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/docs/building-apps/build-properties.md
[gh-tf]: https://github.com/dotnet/macios/blob/main/docs/preparing-your-app-for-testflight.md
[gh-17829]: https://github.com/dotnet/macios/issues/17829
[gh-rel]: https://github.com/dotnet/macios/releases
[core-os]: https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md
[sdk-targets]: https://github.com/dotnet/sdk/blob/release/11.0.1xx/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.targets
[src-shared]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.Shared/Xamarin.Shared.targets
[src-compute]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/ComputeCodesignItems.cs
[src-codesign]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/Codesign.cs
[src-installer]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/CreateInstallerPackage.cs
[src-ent]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileEntitlements.cs
[src-manifest]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileAppManifest.cs
[src-publish]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/dotnet/targets/Xamarin.Shared.Sdk.Publish.targets
[src-props]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/dotnet/targets/Xamarin.Shared.Sdk.props
[src-sdktargets]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/dotnet/targets/Xamarin.Shared.Sdk.targets
[src-plist]: https://github.com/dotnet/macios/blob/xcode27.2-net11.0/builds/Versions-MacCatalyst.plist.in
[nuget-mac-manifest]: https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.maccatalyst.manifest-11.0.100/index.json
[nuget-mac-manifest-pkg]: https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.maccatalyst.manifest-11.0.100/27.2.11039/microsoft.net.sdk.maccatalyst.manifest-11.0.100.27.2.11039.nupkg
[nuget-coreclr-cat]: https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.maccatalyst-arm64/index.json
[nuget-maui-manifest]: https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.maui.manifest-11.0.100/index.json
[nuget-mac-sdk]: https://api.nuget.org/v3-flatcontainer/microsoft.maccatalyst.sdk.net11.0_27.2/index.json
[nuget-mac-sdk-pkg]: https://api.nuget.org/v3-flatcontainer/microsoft.maccatalyst.sdk.net11.0_27.2/27.2.11039/microsoft.maccatalyst.sdk.net11.0_27.2.27.2.11039.nupkg
[nuget-maui-controls]: https://api.nuget.org/v3-flatcontainer/microsoft.maui.controls/index.json
[nuget-templates]: https://api.nuget.org/v3-flatcontainer/microsoft.maui.templates.net11/11.0.0/microsoft.maui.templates.net11.11.0.0.nupkg
[gh-readme]: https://github.com/actions/runner-images
[gh-mac26]: https://github.com/actions/runner-images/blob/main/images/macos/macos-26-arm64-Readme.md
[gh-xc27]: https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md
[gh-14404]: https://github.com/actions/runner-images/issues/14404
[gh-setupdotnet]: https://github.com/actions/setup-dotnet
