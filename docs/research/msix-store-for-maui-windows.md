# MSIX và Microsoft Store cho app MAUI trên Windows, giữ Package Identity (research cho #326)

Ngày: 2026-11-26. Ticket: #326, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

**Câu hỏi.** Trí Thư phát hành MSIX x64 self-contained đóng bằng `pack-msix.ps1` (winapp CLI) và chỉ đi qua Microsoft Store (ADR `docs/adr/0005-store-only-msix-release.md`). Sau khi đầu Windows chuyển sang MAUI (WinUI 3, Windows App SDK) trên .NET 11, ticket hỏi: (1) đóng gói MSIX cho Store bằng cách nào, `pack-msix.ps1` với winapp còn dùng được không, runtime Windows App SDK đi self-contained hay thành gói phụ thuộc do Store cài; (2) giữ Package Name và Publisher thì `%APPDATA%` ảo hoá, thư mục Library, các SQLite và key API mã hoá DPAPI còn không; (3) mức Windows tối thiểu có đổi không; (4) người dùng bản WPF nhận bản MAUI như một bản cập nhật thường hay phải cài lại.

**Cách làm.** Đọc code và manifest trong repo (chỉ đọc). Tải các gói NuGet mà MAUI 11.0.10 kéo theo về thư mục tạm để đọc `.nuspec`, targets, props và `AppxManifest.xml` của gói framework. Đọc docs sống trên learn.microsoft.com (nhiều trang cập nhật 2026-11) và mã nguồn `dotnet/maui` tại commit ghi trong nuspec, `microsoft/WinAppCli` trên nhánh `main`. Không build, không cài, không chạy thử bản MSIX nào, không đụng dữ liệu người dùng. **[Fact]** là điều tài liệu, mã nguồn hoặc file ghi; **[Suy luận]** là nhận định của tôi.

## Tóm tắt

- **Có hai đường chính thức để ra MSIX.** Đường A: `dotnet publish -p:WindowsPackageType=MSIX`, SDK tự sinh gói. Đường B: publish ra thư mục rồi `winapp package`, là guide riêng của Microsoft cho MAUI. **[Fact]** [MAUI: publish bằng CLI][ms-publish-cli], [winapp + MAUI][winapp-maui]
- **`pack-msix.ps1` và winapp vẫn dùng được** (đường B), nhưng lệnh publish phải thêm `-f net11.0-windows10.0.19041.0` và `-p:WindowsPackageType=None`; phần stamp version, copy `Assets`, `winapp cert generate`, `winapp package` giữ nguyên. **[Suy luận, tin cậy cao]** từ hai guide và script hiện tại
- **Bẫy tên thuộc tính.** Docs MAUI ghi `WindowsPackageType=Package`, nhưng targets MSIX (các bản 1.7.x từ 2025-05 đến 2026-11) chỉ nhận `None`, `MSIX`, `Sparse`. Dùng `MSIX`. **[Fact]** về targets, **chưa chạy thử** build
- **Runtime Windows App SDK của MAUI 11 là nhánh 2.x (`Microsoft.WindowsAppSDK` 2.5.1).** App đóng gói mặc định framework-dependent: manifest khai báo `PackageDependency` vào `Microsoft.WindowsAppRuntime.2` và Store cài giúp. Muốn mang theo thì đặt `WindowsAppSDKSelfContained=true`; đường B mặc định đã self-contained. **[Fact]** [MAUI targets][src-maui-targets], [WASDK: deployment overview][wasdk-deploy-overview]
- **Dữ liệu còn**, với điều kiện giữ đúng `Name`, `Publisher` (phân biệt hoa thường) và `Application Id="App"`, và code tiếp tục dùng `Environment.GetFolderPath(ApplicationData)`. **[Suy luận, tin cậy cao]** từ docs Store và docs MSIX
- **Bẫy `FileSystem.AppDataDirectory`.** Với app packaged, MAUI 11 trả `ApplicationData.Current.LocalFolder.Path` (thư mục `LocalFolder` của `ApplicationData`), khác chỗ dữ liệu hiện tại: app sẽ mở ra thư viện rỗng dù dữ liệu vẫn còn. **[Fact]** [FileSystem.windows.cs][src-fs]
- **Đường dẫn vật lý** dữ liệu là `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Roaming\PdfReaderApp`. Cơ chế ảo hoá là **[Fact]**; đúng chuỗi đường dẫn thì tôi không tìm được trang docs chính thức nào ghi, cần xác nhận bằng smoke test.
- **Key API DPAPI giải mã được.** DPAPI gắn với tài khoản đăng nhập và máy, không liên quan package identity. **[Suy luận]** từ [docs DPAPI][dpapi]. Nhưng `ProtectedData` không nằm trong `Microsoft.NETCore.App`: app WinUI 3 không có `UseWPF` phải thêm gói NuGet `System.Security.Cryptography.ProtectedData`. **[Fact]** đo trên targeting pack .NET 10 ở máy này
- **Mức Windows tối thiểu không đổi.** MAUI trên .NET 11: Windows 10 1809 trở lên; framework package 2.5.1 có `MinVersion="10.0.17763.0"`; manifest hiện tại cũng là 17763. Không ai bị bỏ lại, miễn không nâng `MinVersion`. **[Fact]** [MAUI: Supported platforms][ms-platforms]
- **Bản MAUI tới như một bản cập nhật thường.** Cùng package family, version cao hơn, `MinVersion` không cao hơn; `EntryPoint="Windows.FullTrustApplication"` và `runFullTrust` giữ nguyên. **[Suy luận]** từ [docs Store][store-upload] và [docs manifest][appx-application]
- **Chưa kiểm chứng, phải làm trong smoke test:** nâng cấp thật từ bản WPF sang bản MAUI; đường dẫn dữ liệu trên đĩa; có cần workload `maui-windows` trên CI; app packaged bằng .NET có cần VCLibs không. Xem mục 5.

## 0. Số phiên bản đã kiểm ngày 26/11/2026

| Thứ | Giá trị | Nguồn |
|---|---|---|
| `Microsoft.Maui.Controls` | ổn định mới nhất 11.0.10 (cùng có 11.0.0) | [NuGet][n-controls] |
| `Microsoft.Maui.Core` 11.0.10, nhóm `net11.0-windows10.0.19041` | `Microsoft.WindowsAppSDK` 2.5.1; `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2705; `Microsoft.Windows.SDK.BuildTools.WinApp` 0.7.1; `Microsoft.Graphics.Win2D` 1.4.0; `Microsoft.Web.WebView2` 1.0.4258.31 | [nuspec][n-core] |
| `Microsoft.WindowsAppSDK` 2.5.1 | Base 2.0.4; Foundation 2.3.12; WinUI 2.3.9; Runtime `[2.5.1]` | [nuspec][n-wasdk] |
| `Microsoft.WindowsAppSDK.Base` 2.0.4 | kéo `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.251221100 | [nuspec][n-base] |
| Framework package | `Name="Microsoft.WindowsAppRuntime.2"`, `Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"`, Version 2.5.1.0, `Framework=true`, `MinVersion="10.0.17763.0"`; file x64 khoảng 47,5 MB | `tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.2.msix` trong [gói Runtime][n-runtime] |
| Windows App SDK Stable | 2.7.0 (17/11/2026); vòng đời nhánh 2.0: 29/4/2026 đến 29/4/2027; nhánh 1.8 hết hỗ trợ 24/9/2026 | [release channels][wasdk-channels] |
| winapp CLI | v0.8.0 (07/11/2026), docs ghi "public preview"; action `microsoft/setup-WinAppCli` có tag tới v0.3.0 (repo đang dùng `@v0.2`) | [WinAppCli releases][gh-winapp-rel], [winapp CLI][winapp-index] |

Không lấy số nào từ trí nhớ: phiên bản gói đọc từ NuGet, thuộc tính đọc thẳng từ file trong gói.

## 1. Đóng gói MSIX cho Store với .NET 11

### 1.1 MAUI 11 chạy trên Windows App SDK nhánh 2.x

**[Fact]** `Microsoft.Maui.Core` 11.0.10 phụ thuộc `Microsoft.WindowsAppSDK` 2.5.1 cho TFM Windows. Wiki của MAUI cũng nói cách kiểm: "To see the version of WinAppSDK that we depend on, you can check the NuGet dependency" ([Release Versions][maui-release-versions]). Nhánh 2.x là nhánh Stable hiện hành; 1.8 đã hết hỗ trợ 24/9/2026 ([release channels][wasdk-channels]).

### 1.2 Đường A: để SDK sinh MSIX

**[Fact]** Docs "Use the CLI to publish packaged apps for Windows" (cập nhật 2026-02-24, có moniker `net-maui-11.0`) hướng dẫn đặt thuộc tính trong csproj rồi chạy `dotnet publish -f <tfm> -c Release -p:RuntimeIdentifierOverride=win-x64`; gói ký sẵn nằm ở `bin\Release\<tfm>\<rid>\AppPackages\<appname>\`. Ví dụ trong docs vẫn ghi `net10.0-windows10.0.19041.0`; từ .NET 10 phải dùng RID portable (`win-x64`), không còn `win10-x64`; publish phải trỏ vào project MAUI, không trỏ vào solution ([MAUI: publish bằng CLI][ms-publish-cli]). Với .NET 11, TFM theo template là `net11.0-windows10.0.19041.0` ([template WinUI][src-tpl-csproj]).

**[Fact]** Ký gói bằng `AppxPackageSigningEnabled` và `PackageCertificateThumbprint` (docs), hoặc `PackageCertificateKeyFile` với `PackageCertificatePassword` (targets: `AppxPackageSigningEnabled` tự bật nếu có file key) ([targets MSIX][n-msix]).

**[Fact]** Manifest lấy từ `Package.appxmanifest` của project: với single project là `Platforms\Windows\Package.appxmanifest` ([MAUI: publish overview][ms-publish-overview]); tooling MSIX tự nhận file này ở gốc project khi app không phải `WindowsPackageType=None` ([targets MSIX][n-msix]).

**[Fact]** MAUI chạy task `GeneratePackageAppxManifest` trước khi SDK đóng gói. Task này chỉ ghi đè: `Identity/@Name` khi rỗng hoặc bằng `maui-package-name-placeholder`; `Identity/@Version` khi rỗng hoặc bằng `0.0.0.0` (ghép `ApplicationDisplayVersion` và `ApplicationVersion`); logo và splash khi rỗng hoặc `$placeholder$`. `Publisher` không bị đụng ([GeneratePackageAppxManifest.cs][src-gen-manifest]). **[Suy luận]** Manifest đã có giá trị thật như file hiện tại của Trí Thư đi qua task này gần như nguyên vẹn; vẫn phải stamp version trước build hoặc đặt `Version="0.0.0.0"` rồi truyền `-p:ApplicationDisplayVersion=X.Y.Z -p:ApplicationVersion=0`.

### 1.3 Đường B: publish ra thư mục rồi `winapp package`

**[Fact]** Guide "Using winapp CLI with .NET MAUI (Windows)" (cập nhật 2026-08-19) nêu đúng một cạm bẫy riêng của MAUI: manifest nguồn `Platforms/Windows/Package.appxmanifest` chứa token `$placeholder$` mà `winapp package` không giải quyết, nên phải dùng manifest đã sinh ở `obj\...\resizetizer\m\Package.appxmanifest` và truyền `--manifest`, `--executable`. Các bước ([guide][winapp-maui], [test của mẫu chính thức][gh-maui-sample]):

```powershell
dotnet publish <app>.csproj -c Release -f <tfm> -r win-x64 -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true --output .\publish\win-x64
winapp cert generate --manifest <manifest đã sinh> --if-exists overwrite
winapp package .\publish\win-x64 --manifest <manifest đã sinh> --executable <app>.exe --cert .\devcert.pfx
```

**[Fact]** `winapp package` là tên gốc của lệnh; `pack` là alias (`base("package", ...)`, `Aliases.Add("pack")`) ([PackageCommand.cs][src-winapp-cmd]). Docs hiện viết `pack`; script của Trí Thư dùng `package`, cả hai đều hợp lệ.

**[Fact]** Ở chế độ thư mục, `winapp pack` chỉ resolve `$targetnametoken$` khi manifest còn token (qua `--executable` hoặc tự tìm đúng một file `.exe`); `$targetentrypoint$` luôn được thay bằng `Windows.FullTrustApplication`; có `--no-sign` cho bản nộp Store ([winapp usage][winapp-usage]). **[Suy luận]** Manifest gốc của Trí Thư không có token nào và `Executable="PdfReaderApp.exe"` đã cụ thể, nên `pack-msix.ps1` không cần `--manifest` hay `--executable`; cách làm hiện tại (copy manifest vào thư mục publish, stamp version, copy `Assets`) vẫn đúng.

### 1.4 Bẫy giá trị của `WindowsPackageType`

**[Fact]** Docs MAUI ghi `<WindowsPackageType>Package</WindowsPackageType>` ([publish-cli][ms-publish-cli]). **[Fact]** Target `_ValidateWindowsPackageType` trong `Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets` báo lỗi "'...' is not valid for the WindowsPackageType property. Value must be one of the following: None,MSIX,Sparse"; tôi kiểm ba bản 1.7.20250508.1, 1.7.251221100, 1.7.261119100, kết quả giống nhau. MAUI targets cũng so sánh đúng với `'MSIX'` ([src-maui-targets][src-maui-targets]). **[Suy luận]** Docs có thể đang lỗi; dùng `MSIX` cho khớp tooling. Chưa chạy thử.

### 1.5 Runtime Windows App SDK đi theo kiểu nào

**[Fact]** Targets của MAUI 11.0.10 chỉ đặt `WindowsAppSDKSelfContained=true` khi `WindowsPackageType=None`, kèm comment "By default, unpackaged .NET MAUI apps are fully self-contained" ([src-maui-targets][src-maui-targets]). **[Fact]** `Microsoft.WindowsAppSDK.Runtime.props` đặt `MicrosoftWindowsAppSDKPackageDir`; `Base.targets` chỉ mặc định self-contained khi thuộc tính này rỗng, và báo lỗi nếu app không self-contained mà thiếu gói Runtime ([n-base][n-base]). **[Fact]** Docs: "Framework-dependent deployment is the default deployment mode" và "The Windows App SDK runtime and Framework package are installed automatically by framework-dependent apps that are packaged" ([deploy overview][wasdk-deploy-overview]). **[Suy luận, tin cậy cao]** Kết quả cho Trí Thư:

| Đường | `WindowsPackageType` | Runtime WASDK | Ai cài runtime |
|---|---|---|---|
| A (SDK sinh MSIX), không đặt gì | `MSIX` | framework-dependent | Store, qua `PackageDependency` `Microsoft.WindowsAppRuntime.2` 2.5.1.0 |
| A, đặt `WindowsAppSDKSelfContained=true` | `MSIX` | self-contained | nằm trong MSIX của app |
| B (winapp), theo guide | `None` | self-contained | nằm trong MSIX của app |

**[Fact]** Gói Runtime khai báo `AppxPackageRegistration` cho `Microsoft.WindowsAppRuntime.2` với đúng Publisher và version 2.5.1.0 ([n-runtime][n-runtime]); docs xác nhận "the generated package manifest contains a PackageDependency element that specifies a dependency on the framework package" ([deploy-packaged-apps][wasdk-packaged]). **[Fact]** Đổi lại giữa hai kiểu ([deploy overview][wasdk-deploy-overview], [deployment architecture][wasdk-arch]): framework-dependent cho gói nhỏ và được vá tự động ("all framework-dependent apps are updated to the new version without themselves having to redistribute a copy"); self-contained cho app tự giữ phiên bản nhưng "You're responsible for integrating servicing updates of the Windows App SDK into your app". Với app packaged framework-dependent, auto-initializer `WindowsAppSdkDeploymentManagerInitialize` mặc định chạy `DeploymentManager.Initialize`, tức cài gói Main và Singleton ([project properties][wasdk-props], [deploy-packaged-apps][wasdk-packaged]).

**[Fact]** `Microsoft.WindowsAppSDK.AppXReference.props` không tự thêm `PackageDependency` VCLibs, nhưng docs ghi "For packaged apps, the VCLibs framework package dependency is a requirement" ([deploy-packaged-apps][wasdk-packaged]). Xem mục 5.

### 1.6 `pack-msix.ps1` và `release.yml`: giữ gì, đổi gì

| File | Giữ nguyên | Phải đổi (đường B) |
|---|---|---|
| `pack-msix.ps1` | stamp `X.Y.Z.0` vào manifest; copy `Assets`; `winapp cert generate --manifest`; `winapp package <thư mục> --output ... --cert ... --cert-password ...` | lệnh `dotnet publish`: trỏ sang project đầu MAUI, thêm `-f net11.0-windows10.0.19041.0` và `-p:WindowsPackageType=None`; giữ `-r win-x64 --self-contained true`; thêm `--executable` nếu tên exe đổi |
| `.github/workflows/release.yml` | step Test chặn đóng gói; `microsoft/setup-WinAppCli`; upload artifact; GitHub Release | `dotnet-version: 10.0.x` sang `11.0.x`; cân nhắc `setup-WinAppCli@v0.3`; workload xem mục 5 |
| `Package.appxmanifest` | `Identity`, `Application Id="App"`, `EntryPoint="Windows.FullTrustApplication"`, `runFullTrust`, `TargetDeviceFamily` | `Executable` nếu tên exe đổi; chỉ thêm `PackageDependency` khi chọn framework-dependent mà không dùng đường A |

**[Fact]** Template multiproject của MAUI 11 đặt `WindowsPackageType=None` ngay trong csproj, kèm comment "To develop, package, and publish an app to the Microsoft Store, see: https://aka.ms/MauiTemplateUnpackaged" ([template csproj][src-tpl-csproj]).

**[Suy luận] Đề xuất:** đi đường B với đường lối thay đổi ít nhất: giữ `pack-msix.ps1`, winapp và manifest gốc làm nguồn duy nhất của Package Identity; publish `WindowsPackageType=None` nên WASDK self-contained; không thêm `Platforms/Windows/Package.appxmanifest` thứ hai. Lý do: khớp guide chính thức cho MAUI, không cần cấu hình ký qua MSBuild, bản WPF cũ và bản MAUI mới đều không có gói phụ thuộc nên lần nâng cấp đầu tiên ít biến số. Giá phải trả: mỗi lần MAUI lên bản mang WASDK khác thì gói to thêm (tham khảo: gói framework x64 nén 47,5 MB) và chủ repo phải tự nâng WASDK. Nếu muốn Store quản runtime và có bản vá không cần phát hành app thì chọn đường A, mặc định framework-dependent.

Nguồn: [MAUI: publish bằng CLI][ms-publish-cli], [MAUI: publish overview][ms-publish-overview], [MAUI: What's new .NET 11][ms-whatsnew], [WASDK: deployment overview][wasdk-deploy-overview], [WASDK: packaged apps][wasdk-packaged], [WASDK: self-contained][wasdk-selfcontained], [WASDK: project properties][wasdk-props], [winapp + MAUI][winapp-maui], [winapp usage][winapp-usage], [mẫu MAUI của WinAppCli][gh-maui-sample], [targets MAUI][src-maui-targets], [GeneratePackageAppxManifest.cs][src-gen-manifest], [PackageCommand.cs][src-winapp-cmd].

## 2. Giữ Package Identity thì dữ liệu người dùng còn không

### 2.1 Package Identity quyết định nơi dữ liệu nằm

**[Fact]** Partner Center ghi ba giá trị bắt buộc trong manifest: `Package/Identity/Name`, `Package/Identity/Publisher`, `Package/Properties/PublisherDisplayName`; ba giá trị này "declare the identity of your app, establishing the 'package family' to which all of its packages belong" ([Store: app identity][store-identity]). `Name` "is case-sensitive" và `Publisher` phải khớp chủ thể của certificate ký ([Identity element][appx-identity]). **[Fact]** `Application/@Id` "should not be changed after the app has been published to the Microsoft Store" ([Application element][appx-application]).

**[Fact]** Manifest hiện tại: `Name="PaLabs.TrTh" Publisher="CN=2A5A0F21-4481-44FF-B9A2-EDA1EE68295E"` (dòng 6), `Application Id="App" Executable="PdfReaderApp.exe" EntryPoint="Windows.FullTrustApplication"` (dòng 19). **[Suy luận, tin cậy cao]** Giữ nguyên hai chuỗi và `Id="App"` thì bản MAUI thuộc cùng package family với bản WPF, nên dùng cùng kho dữ liệu ảo.

### 2.2 `%APPDATA%` ảo hoá về đâu

**[Fact]** Từ Windows 10 1903, file và thư mục mới tạo dưới `AppData` (gồm `Roaming`) "are written to a private per-user, per-app location; but merged at runtime to appear in the real AppData location"; khi mở file, hệ thống mở ở vị trí riêng trước, mới quay về `AppData` thật; ghi vào thư mục gói bị cấm ([behind the scenes][msix-behind]). **[Fact]** Dữ liệu giữ qua bản cập nhật: "The system generally preserves the contents of these data stores when the user installs an update to your app", chỉ xoá khi gỡ app ([app data][app-data]); registry ảo "are kept during package upgrade, and deleted only when the app is removed entirely" ([behind the scenes][msix-behind]).

**[Fact]** Hai bản là cùng loại tiến trình: docs xếp WinUI 3 và Desktop Bridge vào một loại, `uap10:RuntimeBehavior="packagedClassicApp"`; `EntryPoint="Windows.FullTrustApplication"` tương đương `TrustLevel="mediumIL"` ([behind the scenes][msix-behind], [Application element][appx-application]). Template WinUI 3 mặc định là full trust với `EntryPoint="$targetentrypoint$"` và `runFullTrust` ([MSIX AppContainer apps][msix-container]); tooling MSIX mặc định thay token bằng `Windows.FullTrustApplication` ([targets MSIX][n-msix]). **[Suy luận]** Không có khác biệt về ảo hoá file system giữa bản WPF packaged và bản MAUI packaged. Lưu ý docs dùng chữ "virtualized apps" cho các mục đó; ADR 0005 của repo cũng ghi "AppData ảo hóa" khi nói bản MSIX, tức full trust cũng nằm trong nhóm này.

**[Suy luận, chưa kiểm chứng]** Vị trí riêng trên đĩa là `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Roaming\PdfReaderApp`. Tôi không tìm được trang docs chính thức nào ghi đúng chuỗi này (đã dò 10 trang ứng viên). Cần xác nhận trong smoke test bằng cách ghi log `AppDataDirectory.Current()` và nhìn thư mục trên đĩa.

**[Fact]** Docs cảnh báo "Roaming data and settings is no longer supported as of Windows 11", nói về kho roaming của `ApplicationData` ([app data][app-data]). **[Suy luận]** Cơ chế chuyển hướng thư mục cho app desktop là khác, bản WPF hiện tại đã chạy theo cách đó trên Windows 11.

### 2.3 Bẫi: `FileSystem.AppDataDirectory` của MAUI

**[Fact]** Với app packaged, `FileSystem.AppDataDirectory` trả `ApplicationData.Current.LocalFolder.Path` và `CacheDirectory` trả `ApplicationData.Current.LocalCacheFolder.Path`; chỉ app unpackaged mới rơi về `%LOCALAPPDATA%\<Publisher>\<Package>\Data` ([FileSystem.windows.cs][src-fs]). **[Suy luận, tin cậy cao]** `LocalFolder` không phải `%APPDATA%\PdfReaderApp`, nên nếu đầu MAUI thay `AppDataDirectory` bằng API của MAUI thì app mở thư viện rỗng. Phải giữ `AppDataDirectory.Resolve` (`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` cộng `PdfReaderApp`, `src/PdfReaderApp/Core/AppDataDirectory.cs`). Lớp này chỉ dùng `System.IO`, chuyển sang project lõi nguyên vẹn.

### 2.4 Library, SQLite, `vec0.dll`

**[Fact]** `DefaultServices.cs` đặt mọi thứ dưới `dataDir`: `index.db`, `chats.db`, `notes.db`, `compositions.db`, `workspaces.db`, `library.db`, `library\` và `library\thumbs\` (dòng 34 đến 57); `vec0.dll` nạp qua `Path.Combine(AppContext.BaseDirectory, "vec0.dll")` (dòng 36). **[Suy luận]** Đường dẫn dữ liệu không đổi (mục 2.1 đến 2.3) thì các file này không đổi chỗ. `AppContext.BaseDirectory` trong MSIX trỏ vào thư mục cài đặt chỉ đọc, file `vec0.dll` nằm ở gốc gói như bản WPF hiện tại; đọc được, chỉ ghi mới bị cấm ([behind the scenes][msix-behind]). Cần giữ item copy `vec0.dll` sang output trong project đầu MAUI.

### 2.5 Key API mã hoá DPAPI

**[Fact]** `WindowsSettingsService` dùng `ProtectedData.Protect` và `Unprotect` với `DataProtectionScope.CurrentUser`, optional entropy `null`, ghi `settings.dat`, `settings.gemini.dat`, `settings.claude.dat` (dòng 31 đến 60, 116 đến 121). **[Fact]** DPAPI: "Typically, only a user with the same logon credential as the user who encrypted the data can decrypt the data. In addition, the encryption and decryption usually must be done on the same computer" ([CryptProtectData][dpapi]). Không có chữ nào về package identity.

**[Suy luận, tin cậy cao]** Bản WPF trên Store hiện đã chạy packaged, full trust, cùng user và cùng máy; bản MAUI cũng vậy. `ProtectedData.Unprotect(..., CurrentUser)` đọc lại đúng ba file đó. **[Fact]** Nhưng `System.Security.Cryptography.ProtectedData.dll` chỉ có trong `Microsoft.WindowsDesktop.App.Ref`, không có trong `Microsoft.NETCore.App.Ref` (đo trên targeting pack 10.0.7 ở máy này); gói `System.Security.Cryptography.ProtectedData` có bản 11.0.0 trên NuGet. **[Suy luận]** Đầu MAUI (không `UseWPF`) phải tham chiếu gói NuGet này, nếu không lỗi biên dịch. Phần lưu key cho macOS thuộc ticket khác.

Nguồn: [Store: app identity][store-identity], [Identity element][appx-identity], [Application element][appx-application], [behind the scenes][msix-behind], [app data][app-data], [MSIX AppContainer apps][msix-container], [FileSystem.windows.cs][src-fs], [CryptProtectData][dpapi], `src/PdfReaderApp/Core/AppDataDirectory.cs`, `src/PdfReaderApp/ViewModels/DefaultServices.cs`, `src/PdfReaderApp/Services/WindowsSettingsService.cs`.

## 3. Mức Windows tối thiểu

**[Fact]** Docs "Supported platforms for .NET MAUI apps", moniker `net-maui-11.0`: "Windows 11 and Windows 10 version 1809 or higher, using Windows UI Library (WinUI) 3" ([link][ms-platforms]). **[Fact]** Windows App SDK "is currently backward compatible to Windows 10 version 1809" nhưng Microsoft chỉ hỗ trợ trên các bản Windows còn hạn hỗ trợ ([release channels][wasdk-channels]). **[Fact]** Framework package 2.5.1 khai báo `MinVersion="10.0.17763.0"` (mục 0). **[Fact]** Template MAUI 11 đặt `TargetPlatformMinVersion` và manifest ở `10.0.17763.0` ([template csproj][src-tpl-csproj], [template manifest][src-tpl-manifest]). **[Fact]** .NET 11 hỗ trợ Windows 10 1607 (Enterprise), 1809 (Enterprise), 21H2 và Windows 11 24H2 trở lên ([.NET 11 supported OS][dotnet-os]).

**[Fact]** Manifest hiện tại: `TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0"` (dòng 13), tức đúng Windows 10 1809. **[Suy luận, tin cậy cao]** Chừng nào bản MAUI giữ `MinVersion` này thì không người dùng nào bị bỏ lại. TFM `net11.0-windows10.0.19041.0` là phiên bản SDK biên dịch, không phải mức hệ điều hành.

**[Fact]** Rủi ro duy nhất là nâng `MinVersion`: Store không đưa gói cho máy có phiên bản thấp hơn `MinVersion` ([upload packages][store-upload]). **[Fact]** Template MAUI khai báo cả `Windows.Universal` lẫn `Windows.Desktop`; Store khuyên "update the TargetDeviceFamily element in your manifest to target only the device family that you want to support (i.e., Windows.Desktop)" ([upload packages][store-upload]). **[Suy luận]** Giữ dòng `TargetDeviceFamily` hiện tại, không chép theo template.

**[Fact]** Thực tế: Windows 10 1809 Home và Pro hết hỗ trợ 11/11/2020, 22H2 Home và Pro hết 15/10/2025 ([lifecycle][win10-life]); LTSC 2019 (1809) còn hỗ trợ tới tháng 1/2029 ([lifecycle][ltsc-life]). **[Fact]** Giới hạn cũ vẫn còn: x64 chỉ chạy trên Windows on ARM qua giả lập, và giả lập x64 chỉ có trên Windows 11 ([deploy overview][wasdk-deploy-overview]).

**Chưa kiểm chứng:** hành vi thật của MAUI 11 và Win2D trên Windows 10 1809 đến 1903; không có bản thử nào.

Nguồn: [MAUI: Supported platforms][ms-platforms], [release channels][wasdk-channels], [.NET 11 supported OS][dotnet-os], [upload packages][store-upload], [Windows 10 Home and Pro lifecycle][win10-life], [Windows 10 Enterprise LTSC 2019 lifecycle][ltsc-life], `Package.appxmanifest`.

## 4. Bản MAUI tới người dùng như một bản cập nhật thường?

**[Fact]** Store "will automatically provide each customer with the package that works best for their device"; ai đã cài app "will get any updates you submit"; chọn theo version cao nhất mà máy chạy được ([upload packages][store-upload], [package requirements][store-req]). **[Fact]** Version phải có bốn phần, phần cuối bằng 0; Store ký lại gói bằng certificate của Microsoft ([package requirements][store-req]). **[Fact]** Với app packaged framework-dependent, Store cài runtime và framework package ([deploy overview][wasdk-deploy-overview]); framework package "is deployed to end users through the Microsoft Store" ([deployment architecture][wasdk-arch]).

**[Suy luận, tin cậy cao]** Store và bộ máy triển khai MSIX chỉ nhìn package family, version, kiến trúc và `MinVersion`; chúng không phân biệt WPF với WinUI 3 hay runtime .NET nằm trong gói. Vì vậy bản MAUI thay bản WPF tại chỗ như mọi bản phát hành trước, kho dữ liệu theo package family giữ nguyên. Bản WPF hiện tại đã là self-contained .NET, bản MAUI cũng vậy, nên không phát sinh yêu cầu mới về .NET runtime.

Khai báo trong manifest: gì giữ, gì khác.

| Mục | Hiện tại (WPF) | Bản MAUI | Ghi chú |
|---|---|---|---|
| `Identity/@Name`, `@Publisher` | `PaLabs.TrTh`, `CN=2A5A...` | giữ nguyên | quyết định package family và dữ liệu |
| `Identity/@Version` | stamp `X.Y.Z.0` | giữ nguyên cách làm | phần cuối phải là 0 |
| `Application/@Id` | `App` | giữ nguyên | không đổi sau khi đã phát hành |
| `Application/@Executable` | `PdfReaderApp.exe` | theo tên exe của đầu MAUI | đặt `AssemblyName` hoặc sửa thuộc tính |
| `Application/@EntryPoint` | `Windows.FullTrustApplication` | giống | mặc định của tooling MSIX và winapp |
| `rescap:Capability` | `runFullTrust` | giống | mặc định của template WinUI 3 |
| `Dependencies` | chỉ `TargetDeviceFamily` | giống nếu self-contained; thêm `PackageDependency` `Microsoft.WindowsAppRuntime.2` nếu framework-dependent | chỉ ở đường A |
| `TargetDeviceFamily` | chỉ `Windows.Desktop`, 17763 | giữ nguyên | không thêm `Windows.Universal` |

**Chưa kiểm chứng:** một MSIX ký bằng dev cert có cài đè được lên bản do Store ký không. Cách kiểm an toàn là trong Windows Sandbox hoặc máy ảo: cài bản WPF dev-sign, rồi cài bản MAUI dev-sign cùng cert, cùng identity, với version cao hơn.

Nguồn: [upload packages][store-upload], [package requirements][store-req], [package management][store-mgmt], [Application element][appx-application], [deploy overview][wasdk-deploy-overview], [deployment architecture][wasdk-arch], [MSIX AppContainer apps][msix-container], `Package.appxmanifest`, `pack-msix.ps1`.

## 5. Chưa kiểm chứng và việc cần làm trong smoke test

Ticket không được chạy thử, nên các điểm sau phải kiểm bằng smoke test (xem "MSIX smoke test" trong `GLOSSARY.md`):

1. **Đường dẫn dữ liệu trên đĩa.** Ghi log `AppDataDirectory.Current()` ở bản WPF packaged và bản MAUI packaged; mở thư mục dưới `%LOCALAPPDATA%\Packages\` để xác nhận `LocalCache\Roaming\PdfReaderApp`.
2. **Nâng cấp tại chỗ.** Bản WPF dev-sign cài trước, bản MAUI dev-sign cùng identity và version cao hơn cài sau; kiểm Library, sáu SQLite, ba file key (key API còn đọc được), `vec0.dll` nạp được (RAG phạm vi Toàn Workspace chạy).
3. **`WindowsPackageType`.** Chạy thật với `MSIX` và với `Package` để biết docs MAUI đúng hay targets đúng.
4. **Framework-dependent ở đường B.** Chưa rõ `winapp package` ở chế độ thư mục có tự thêm `PackageDependency` đúng khi publish không self-contained hay không; guide chỉ mô tả self-contained.
5. **VCLibs.** Docs nói app packaged cần VCLibs framework package; manifest gói framework 2.5.1 không khai báo phụ thuộc này và bản WPF hiện tại không có. Nếu thiếu, docs liệt kê lỗi `0x80073CF3` ("A required dependency package is missing or incompatible") ([deploy-packaged-apps][wasdk-packaged]).
6. **Workload trên CI.** Guide yêu cầu `dotnet workload install maui-windows`; chưa rõ build chỉ Windows từ gói NuGet có bắt buộc không ([winapp + MAUI][winapp-maui]).
7. **WebView2.** MAUI 11 kéo `Microsoft.Web.WebView2` (mục 0). Nếu đầu MAUI dùng `WebView`, `HybridWebView` hoặc `BlazorWebView` thì có thể cần WebView2 Runtime trên máy người dùng; tôi chưa tra nguồn cho điều kiện này và chưa kiểm tra Trí Thư có dùng không.
8. **Windows 10 cũ.** Nếu chủ repo quan tâm tới LTSC 2019, thử trên máy ảo 1809.

**[Suy luận] Hai việc nên ghi vào ADR phát hành sau migration:** (a) một manifest nguồn duy nhất mang Package Identity, và Windows luôn đọc dữ liệu qua `AppDataDirectory`, không qua `FileSystem.AppDataDirectory`; (b) chọn một trong hai kiểu runtime WASDK và ghi lý do.

[ms-publish-cli]: https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-cli
[ms-publish-overview]: https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/overview
[ms-platforms]: https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms
[ms-whatsnew]: https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11
[maui-release-versions]: https://github.com/dotnet/maui/wiki/Release-Versions
[wasdk-deploy-overview]: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview
[wasdk-packaged]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps
[wasdk-selfcontained]: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
[wasdk-arch]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture
[wasdk-channels]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
[wasdk-props]: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties
[msix-behind]: https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes
[msix-container]: https://learn.microsoft.com/en-us/windows/msix/msix-container
[app-data]: https://learn.microsoft.com/en-us/windows/apps/develop/data/store-and-retrieve-app-data
[appx-application]: https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-application
[appx-identity]: https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-identity
[store-identity]: https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details
[store-req]: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements
[store-upload]: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages
[store-mgmt]: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-management
[winapp-index]: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/
[winapp-usage]: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/usage
[winapp-maui]: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/maui
[gh-winapp-rel]: https://github.com/microsoft/WinAppCli/releases
[gh-maui-sample]: https://github.com/microsoft/WinAppCli/tree/main/samples/maui-app
[src-winapp-cmd]: https://github.com/microsoft/WinAppCli/blob/main/src/winapp-CLI/WinApp.Cli/Commands/PackageCommand.cs
[src-maui-targets]: https://github.com/dotnet/maui/blob/87182d7bcbbc9af30bc6dd81991aa6fd1ab3cd35/src/Controls/src/Build.Tasks/nuget/buildTransitive/net6.0-windows10.0.17763.0/Microsoft.Maui.Sdk.Windows.targets
[src-gen-manifest]: https://github.com/dotnet/maui/blob/87182d7bcbbc9af30bc6dd81991aa6fd1ab3cd35/src/SingleProject/Resizetizer/src/GeneratePackageAppxManifest.cs
[src-fs]: https://github.com/dotnet/maui/blob/87182d7bcbbc9af30bc6dd81991aa6fd1ab3cd35/src/Essentials/src/FileSystem/FileSystem.windows.cs
[src-tpl-csproj]: https://github.com/dotnet/maui/blob/87182d7bcbbc9af30bc6dd81991aa6fd1ab3cd35/src/Templates/src/templates/maui-multiproject/MauiApp.1.WinUI/MauiApp.1.WinUI.csproj
[src-tpl-manifest]: https://github.com/dotnet/maui/blob/87182d7bcbbc9af30bc6dd81991aa6fd1ab3cd35/src/Templates/src/templates/maui-multiproject/MauiApp.1.WinUI/Package.appxmanifest
[n-controls]: https://www.nuget.org/packages/Microsoft.Maui.Controls
[n-core]: https://www.nuget.org/packages/Microsoft.Maui.Core/11.0.10
[n-wasdk]: https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1
[n-base]: https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Base/2.0.4
[n-msix]: https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/1.7.251221100
[n-runtime]: https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Runtime/2.5.1
[dpapi]: https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata
[dotnet-os]: https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.md
[win10-life]: https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro
[ltsc-life]: https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019
