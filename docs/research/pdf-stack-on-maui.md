# Ngăn xếp PDF trên MAUI: PDFium, Skia, bỏ System.Drawing (research cho #324)

Ngày: 2026-11-26. Ticket: #324, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Câu hỏi. Lõi render của Trí Thư dựa vào ba thứ gắn với Windows: `PdfiumViewer.Net.WPF` 3.0.4 (nạp, render, toạ độ chữ cho Ghost Mapping), SkiaSharp 3.119.4 qua `SkiaSharp.Views.WPF`, và `System.Drawing` làm bước trung gian trong `Core/RenderEngine.cs`. Research trả lời bốn việc: (1) app gọi đúng API nào của PdfiumViewer, gói này có bản cho MAUI, Mac Catalyst, WinUI 3 không, không có thì thay bằng gì; (2) `SkiaSharp.Views.Maui` đã có cho .NET 11 chưa, chọn `SKCanvasView` hay `SKGLView`, vấn đề hiệu năng đã biết; (3) bỏ `System.Drawing` bằng cách copy buffer PDFium thẳng sang `SKBitmap` có được không; (4) bảy file trong Core thay `System.Windows` và `System.Drawing` bằng kiểu và cơ chế nào, ảnh hưởng test ra sao.

Quy ước: **[Fact]** là điều nguồn viết ra (link ngay tại chỗ); **[Suy luận]** là nhận định của người viết; không tìm được nguồn thì ghi **chưa kiểm chứng**. Số phiên bản và ngày phát hành lấy trực tiếp từ NuGet (flat container và registration API) trong ngày 26/11/2026.

## Tóm tắt

- **[Fact]** `PdfiumViewer.Net.WPF` 3.0.4 là gói duy nhất của PdfiumViewer.Net: chỉ `net8.0-windows7.0`, tham chiếu WPF và `System.Drawing.Common`. NuGet không có `PdfiumViewer.Net.Core`, `.WinUI`, `.MAUI`. Repo đẩy lần cuối 12/11/2024, không còn bảo trì.
- **[Fact]** App chỉ gọi 11 thành viên của PdfiumViewer: `PdfDocument.Load`, `PageCount`, `Pages`, `Dispose`; `PdfPage.Width`, `Height`, `Size`, `Render`, `GetCountChars`, `GetTextBounds`, `GetText` (cộng hai giá trị enum `PdfRotation.Rotate0`, `PdfRenderFlags.Annotations`). Tìm kiếm, vùng ảnh, metadata đã đi iText từ trước.
- **[Suy luận]** Không có ứng viên "hộp" nào thay được trọn. Đề xuất P/Invoke PDFium trực tiếp (khoảng 25 hàm) trên bản nhị phân của bblanchon, mượn cách làm của PDFtoImage.
- **[Fact]** bblanchon có đủ `win-x64`, `win-arm64`, `osx-arm64` và Mac Catalyst arm64 (dưới dạng `pdfium.framework.zip` trong `bblanchon.PDFium.iOS`), Apache-2.0, bản 160.0.8179 (23/11/2026). Trên Mac Catalyst P/Invoke phải trỏ `@rpath/pdfium.framework/pdfium`.
- **[Fact]** SkiaSharp ổn định mới nhất là 4.157.0; 3.119.4 (app đang dùng) là bản 3.x cuối. `SkiaSharp.Views.Maui.Controls` và `SkiaSharp.Views.Maui.Core` có bản 4.157.0 nhưng TFM cao nhất là `net10.0` và phụ thuộc MAUI 10.0.110; chưa có bản cho .NET 11.
- **[Suy luận]** Gói đó nhiều khả năng vẫn dùng được cho app `net11.0` (NuGet lấy asset `net10.0`), và MAUI 11 tự đã phụ thuộc SkiaSharp 4.150.1. Phải kiểm thật trong prototype vì SkiaSharp build trên Windows App SDK 1.7 còn MAUI 11 dùng 2.5.1.
- **[Suy luận]** Chọn `SKCanvasView` làm mặc định: phần mềm, trên Windows là `SKXamlCanvas`, cùng mô hình `WriteableBitmap` với `SKElement` mà app đang dùng. `SKGLView` trên Windows còn bốn issue crash đang mở trên WinUI 3.
- **[Suy luận]** Bề mặt Skia giữ cỡ viewport như hiện tại, cuộn ảo bằng offset. Giới hạn extent của `ScrollView` MAUI trên WinUI 3 và Mac Catalyst: chưa kiểm chứng.
- **[Fact]** Bỏ được `System.Drawing`: render thẳng vào `SKBitmap(Bgra8888, Premul)` qua `FPDFBitmap_CreateEx` với buffer `GetPixels()`, tô nền trắng đục rồi `FPDF_RenderPageBitmap`. Không cần copy trung gian.
- **[Fact]** PDFium ghi byte theo thứ tự B,G,R,A và Skia `Bgra8888` cũng vậy. Không được dùng `SKImageInfo.PlatformColorType` vì trên macOS nó là `Rgba8888`. PDFium ghi straight alpha; với nền trắng đục thì premultiplied bằng straight.
- **[Fact]** Sáu trong bảy file Core dùng `System.Windows` (`Rect`, `Point`, `Size`, `MessageBox`, `Clipboard`), `RenderEngine` dùng `System.Drawing`. Không file nào trong Core dùng `Dispatcher`.
- **[Suy luận]** Thay bằng kiểu hình học `double` tự viết trong Core (giữ ngữ nghĩa của WPF `Rect`), chuyển `DesktopPrompts` ra lớp ứng dụng, và ở ViewModels thay `Dispatcher` bằng `IDispatcher` của MAUI hoặc `SynchronizationContext`.
- **[Fact]** Test: 3 trên 20 file test trong `Core`, 6 file trong `Controls` và 1 file trong `Services` chạm trực tiếp kiểu WPF hoặc PDFium; 14 file test tham chiếu `System.Windows` hoặc `DispatcherTimer`. Máy dev này chưa có .NET 11 SDK.

## 1. PdfiumViewer.Net trên MAUI và ứng viên thay PDFium

### 1.1 App đang gọi API nào

- **[Fact]** Đọc metadata của `PdfiumViewer.dll` 3.0.4 trong gói và grep `src/PdfReaderApp`: app gọi đúng các thành viên trong bảng. Namespace `PdfiumViewer.Core` không thuần: assembly tham chiếu `PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Drawing.Common`, `System.Drawing.Primitives`, và trả kiểu `System.Windows.Rect`, `System.Windows.Size`, `System.Drawing.Image`. ([nupkg](https://api.nuget.org/v3-flatcontainer/pdfiumviewer.net.wpf/3.0.4/pdfiumviewer.net.wpf.3.0.4.nupkg))

| API PdfiumViewer.Net 3.0.4 | Chỗ gọi trong app | PDFium bên dưới (theo nguồn PdfiumViewer.Net) |
|---|---|---|
| `PdfDocument.Load(Stream)` trả `PdfDocument` | `Controls/Viewer/PdfDocumentSession.cs:54`, `Services/LibraryService.cs:55` | `FPDF_LoadCustomDocument` (callback đọc Stream, giữ stream tới khi Dispose) |
| `PdfDocument.PageCount` (`int`) | `LibraryService.cs:57`, `PdfDocumentSession.cs:33` | `FPDF_GetPageCount` |
| `PdfDocument.Pages[i]` (`IReadOnlyList<PdfPage>`) | `LibraryService.cs:58`, `PdfDocumentSession.cs:71`, `ViewerFrameComposer.cs:90` | `FPDF_LoadPage` (nạp lười khi cần) |
| `PdfDocument.Dispose()` (đóng cả stream) | `PdfDocumentSession.cs:65,97` | `FPDF_CloseDocument` |
| `PdfPage.Width`, `PdfPage.Height` (`double`) | `RenderEngine.cs:16-17`, `LibraryService.cs:82` | `FPDF_GetPageWidth`, `FPDF_GetPageHeight` |
| `PdfPage.Size` (`System.Windows.Size`) | `PdfDocumentSession.cs:71` | như trên |
| `PdfPage.Render(int w, int h, float dpiX, float dpiY, PdfRotation, PdfRenderFlags)` trả `System.Drawing.Image` | `Core/RenderEngine.cs:20` | `FPDFBitmap_CreateEx` (format 4), `FPDFBitmap_FillRect`, `FPDF_RenderPageBitmap`, `FPDF_FFLDraw` |
| `PdfRotation.Rotate0`, `PdfRenderFlags.Annotations` | `RenderEngine.cs:20` | `rotate = 0`, cờ `FPDF_ANNOT` |
| `PdfPage.GetCountChars()` | `Core/PdfObjectManager.cs:23` | `FPDFText_CountChars` |
| `PdfPage.GetTextBounds(int index, int count)` trả `IReadOnlyList<System.Windows.Rect>` | `PdfObjectManager.cs:27` | `FPDFText_CountRects`, `FPDFText_GetRect`, rồi `FPDF_PageToDevice` |
| `PdfPage.GetText(int start, int count)` | `PdfObjectManager.cs:34` | `FPDFText_GetText` |

- **[Fact]** Các thành viên khác của `PdfDocument` và `PdfPage` (`PdfDocument.Save`, `Search`, `GetInformation`, `CreatePrintDocument`, `Bookmarks`; `PdfPage.GetCharIndexAtPos`, `Search`, `GetPageLinks`, `PageToDevice`, `DeviceToPage`) app không gọi. PdfiumViewer chỉ xuất hiện trong 5 file nguồn: `Core/PdfObjectManager.cs`, `Core/RenderEngine.cs`, `Controls/Viewer/PdfDocumentSession.cs`, `Controls/PdfViewerControl.xaml.cs`, `Services/LibraryService.cs`.
- **[Fact]** `Services/PdfMetadataReader.cs`, `Services/PdfImageLocator.cs`, `Services/ITextPdfDocumentService.cs`, `Services/PdfStructureAnalyzer.cs` không dùng PdfiumViewer: `PdfDocument` ở đó là `iText.Kernel.Pdf.PdfDocument`. Vùng ảnh và rect của kết quả tìm lấy từ iText, không từ PDFium.
- **[Fact]** Ghost Mapping hiện chỉ có text (`PdfObjectManager.GhostText`): `MapPage` gọi `GetTextBounds(i, 1)` và `GetText(i, 1)` cho từng ký tự. App không dùng object ảnh hay path của PDFium.
- **[Fact]** PdfiumViewer.Net đổi toạ độ chữ qua `FPDF_PageToDevice` với kích thước trang ép về `int` (`(int)Width`, `(int)Height`), nên `GhostText.Bounds` lệch khoảng 0,05% so với point thật trên trang A4 (595,28 x 841,89 point). ([PdfPage.cs](https://github.com/vrjure/PdfiumViewer/blob/master/src/PdfiumViewer/Core/PdfPage.cs))

### 1.2 Có bản MAUI, Mac Catalyst, WinUI 3 không, còn bảo trì không

- **[Fact]** NuGet chỉ có `PdfiumViewer.Net.WPF` (8 phiên bản, mới nhất 3.0.4, phát hành 12/11/2024). Các id `pdfiumviewer.net`, `pdfiumviewer.net.core`, `pdfiumviewer.net.winui`, `pdfiumviewer.net.maui` đều trả 404. ([flat container](https://api.nuget.org/v3-flatcontainer/pdfiumviewer.net.wpf/index.json))
- **[Fact]** Repo chỉ có project `src/PdfiumViewer` và `src/PdfiumViewer.Demo`. `PdfiumViewer.csproj` khai `net8.0-windows` với `UseWPF`; P/Invoke cứng `[DllImport("pdfium.dll")]`, cộng thêm `user32.dll`, `gdi32.dll`, `kernel32.dll`. ([csproj](https://github.com/vrjure/PdfiumViewer/blob/master/src/PdfiumViewer/PdfiumViewer.csproj), [NativeMethods.Pdfium.cs](https://github.com/vrjure/PdfiumViewer/blob/master/src/PdfiumViewer/Core/NativeMethods.Pdfium.cs))
- **[Fact]** License Apache-2.0, đẩy lần cuối 12/11/2024, 0 issue, 1 star, 1 fork. ([repo](https://github.com/vrjure/PdfiumViewer))
- **[Fact]** `System.Drawing.Common` từ .NET 6 chỉ chạy trên Windows: nền khác ném `PlatformNotSupportedException`, công tắc `System.Drawing.EnableUnixSupport` đã bị bỏ ở .NET 7. Microsoft khuyên chuyển sang SkiaSharp. ([breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only))

### 1.3 So sánh ứng viên

Bốn nhóm API cần so theo ticket: (a) nạp tài liệu và kích thước trang, (b) render ra bitmap, (c) text có toạ độ, (d) object ảnh và path cho Ghost Mapping.

| Ứng viên | Phiên bản hiện tại | .NET 11 | macOS arm64 | Windows | License | Nhóm API đáp ứng được | Nhận xét |
|---|---|---|---|---|---|---|---|
| `PdfiumViewer.Net.WPF` | 3.0.4 (12/11/2024) | Chỉ `net8.0-windows7.0` kèm WPF; không dùng được trong đầu MAUI (WinUI 3) | Không | Có (qua `bblanchon.PDFium.Win32` 130.0.6721) | Apache-2.0 | a, b, c; không có d. b trả `System.Drawing.Image`, c trả `System.Windows.Rect` | Ngừng bảo trì; WPF và GDI+ nằm ngay trong assembly |
| P/Invoke PDFium trên `bblanchon.PDFium.*` | 160.0.8179 (23/11/2026) | Có (gói native, không phụ thuộc TFM) | Có: Mac Catalyst qua `bblanchon.PDFium.iOS`; macOS qua `bblanchon.PDFium.macOS` | Có: `win-x64`, `win-arm64`, `win-x86` | Apache-2.0 | a, b, c, d | Tự viết binding (khoảng 25 hàm) và tự khoá vì PDFium không thread-safe |
| `PDFtoImage` | 5.4.0 (16/8/2026); 6.0.0-preview.2 có `net11.0` | net8.0 đến net10.0 ở bản ổn định | Có (`net10.0-maccatalyst26.0`) | Có | MIT | a (chỉ kích thước trang, không giữ handle tài liệu), b; không có c, d | API tĩnh nhận cả PDF trong mỗi lần gọi (suy luận: nạp lại mỗi lần); là mẫu tham khảo tốt |
| `Docnet.Core` | 2.6.0; phát hành lần cuối 27/9/2023 | netstandard2.0 | Chỉ `osx-arm64`, `osx-x64` dạng dylib; không có Mac Catalyst | `win-x64`, `win-x86` | MIT | a, b, c (theo README); d chưa kiểm chứng | PDFium 5445 đóng sẵn trong gói; repo đẩy lần cuối 13/5/2024 |
| `PDFiumSharp` | 1.0.0 ổn định; gói phát hành lần cuối 22/10/2021 | netstandard1.4 | Không | Chỉ x64, x86 (`pdfium_x64.dll`, `pdfium_x86.dll`) | GitHub API ghi `NOASSERTION`, chưa kiểm chứng | a, b, c (theo README); d chưa kiểm chứng | Chỉ Windows, ngừng từ 2021 |
| `PdfiumCore`, `PdfLibCore` | 4688.0.0; 2.5.0 (12/4/2023) | cũ | chưa kiểm chứng | có | chưa kiểm chứng | chưa kiểm chứng | Số build PDFium 4688 so với 8179 hiện nay; không xét tiếp |

- **[Suy luận]** "Cả bốn nhóm" chỉ có phương án P/Invoke tự viết đáp ứng, vì app cần giữ handle tài liệu để map text theo trang, thứ mà `PDFtoImage` không có.

- **[Fact]** "PDFium qua SkiaSharp" không phải là một gói: PDFium chỉ xuất API C, SkiaSharp chỉ bọc buffer BGRA mà PDFium ghi ra. Đó chính là phương án P/Invoke ở dòng thứ hai.
- **[Fact]** `PDFtoImage` chỉ có `GetPageCount`, `GetPageSize`, `GetPageSizes`, `ToImage`, `ToImages`, `ToImagesAsync`, `SaveJpeg`, `SavePng`, `SaveWebp`. ([PublicAPI](https://github.com/sungaila/PDFtoImage/blob/master/src/PDFtoImage/PublicAPI/net11.0-maccatalyst/PublicAPI.Shipped.txt))
- **[Fact]** PDFium 160 có sẵn API cho object trang: `FPDFPage_CountObjects`, `FPDFPage_GetObject`, `FPDFPageObj_GetType`, `FPDFPageObj_GetBounds`, `FPDFPageObj_GetRotatedBounds`, `FPDFPageObj_HasTransparency`, `FPDFImageObj_GetBitmap`, `FPDFImageObj_GetRenderedBitmap` (trong `fpdf_edit.h` của gói bblanchon).

### 1.4 Bản PDFium nhị phân đi theo kiểu nào

- **[Fact]** `PdfiumViewer.Net.WPF` không chứa binary, phụ thuộc NuGet `bblanchon.PDFium.Win32` 130.0.6721. `PDFtoImage` 5.4.0 phụ thuộc `bblanchon.PDFium.Win32`, `.macOS`, `.Linux`, `.iOS` 152.0.7961 và `SkiaSharp` 4.150.1. `Docnet.Core` và `PDFiumSharp` nhét sẵn binary trong gói (`runtimes/*/native/pdfium.*`; `build/netstandard1.4/pdfium_x64.dll`). ([PDFtoImage](https://api.nuget.org/v3-flatcontainer/pdftoimage/5.4.0/pdftoimage.5.4.0.nupkg), [Docnet.Core](https://api.nuget.org/v3-flatcontainer/docnet.core/2.6.0/docnet.core.2.6.0.nupkg), [PDFiumSharp](https://api.nuget.org/v3-flatcontainer/pdfiumsharp/1.0.0/pdfiumsharp.1.0.0.nupkg))
- **[Fact]** `bblanchon.PDFium.Win32` 160.0.8179 có `runtimes/win-x64`, `win-arm64`, `win-x86` `pdfium.dll`. `bblanchon.PDFium.macOS` có `runtimes/osx-arm64` và `osx-x64` `libpdfium.dylib`. `bblanchon.PDFium.iOS` có `runtimes/maccatalyst-arm64/native/pdfium.framework.zip` và `maccatalyst-x64`, TFM `net8.0-maccatalyst17.4`. ([Win32](https://api.nuget.org/v3-flatcontainer/bblanchon.pdfium.win32/160.0.8179/bblanchon.pdfium.win32.160.0.8179.nupkg), [macOS](https://api.nuget.org/v3-flatcontainer/bblanchon.pdfium.macos/160.0.8179/bblanchon.pdfium.macos.160.0.8179.nupkg), [iOS](https://api.nuget.org/v3-flatcontainer/bblanchon.pdfium.ios/160.0.8179/bblanchon.pdfium.ios.160.0.8179.nupkg))
- **[Fact]** App Store từ chối app iOS và Mac Catalyst có `.dylib` rời; gói iOS chứa framework (nén zip cho Mac Catalyst), .NET SDK tự nhúng, link và sign. P/Invoke phải trỏ `@rpath/pdfium.framework/pdfium` trên iOS và Mac Catalyst. ([README](https://github.com/bblanchon/pdfium-binaries#ios-and-mac-catalyst-apps-on-the-app-store))
- **[Fact]** PDFtoImage làm đúng vậy: `[LibraryImport(LibraryName)]` với `LibraryName` là `"@rpath/pdfium.framework/pdfium"` dưới `#if IOS`, còn lại `"pdfium"`; mọi lời gọi PDFium bọc trong `lock`. ([NativeMethods.LibraryImport.cs](https://github.com/sungaila/PDFtoImage/blob/master/src/PDFtoImage/Internals/NativeMethods.LibraryImport.cs))
- **[Fact]** Header PDFium ghi: "None of the PDFium APIs are thread-safe"; `FPDF_RenderPage` (HDC) chỉ có trên Windows; `FPDF_RenderPageBitmap` và họ `FPDFBitmap_*` dùng được trên mọi nền. (header trong gói bblanchon, file gốc ở [pdfium](https://pdfium.googlesource.com/pdfium/))
- **[Suy luận]** `bblanchon.PDFium.macOS` (dylib `osx-arm64`) chỉ cần khi chạy lõi `net11.0` thuần (test, công cụ dòng lệnh) trên macOS; bản app Mac Catalyst nên dùng framework của `bblanchon.PDFium.iOS` cho cả hai kênh phát hành.

Nguồn: [PdfiumViewer.Net.WPF trên NuGet](https://www.nuget.org/packages/PdfiumViewer.Net.WPF/3.0.4), [vrjure/PdfiumViewer](https://github.com/vrjure/PdfiumViewer), [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries), [PDFtoImage](https://github.com/sungaila/PDFtoImage), [docnet](https://github.com/GowenGit/docnet), [PDFiumSharp](https://github.com/ArgusMagnus/PDFiumSharp)

## 2. SkiaSharp.Views.Maui cho .NET 11

### 2.1 Phiên bản

- **[Fact]** Tên gói đúng là `SkiaSharp.Views.Maui.Controls` (chứa `SKCanvasView`, `SKGLView`) và `SkiaSharp.Views.Maui.Core`. Id `skiasharp.views.maui` và `skiasharp.views.mauicore` trả 404. Ticket ghi `SkiaSharp.Views.MauiCore`, `SkiaSharp.Views.MauiControls`: id thật có dấu chấm trước `Core` và `Controls`.
- **[Fact]** SkiaSharp 4.157.0 là bản ổn định mới nhất (18/11/2026); 3.119.4 là bản 3.x cuối; 4.148.0 là bản v4 ổn định đầu (22/6/2026). ([flat container](https://api.nuget.org/v3-flatcontainer/skiasharp/index.json), [4.148.0](https://mono.github.io/SkiaSharp/docs/releases/4.148.0.html))
- **[Fact]** `SkiaSharp.Views.Maui.Controls` 4.157.0 có TFM `net9.0`, `net10.0` và `net10.0-android36.0`, `net10.0-ios26.0`, `net10.0-maccatalyst26.0`, `net10.0-windows10.0.19041`; phụ thuộc `Microsoft.Maui.Controls` và `Microsoft.Maui.Core` 10.0.110 (nhóm net10), 9.0.120 (nhóm net9). Bản 4.159.0-rc.1 (18/11/2026) vẫn chưa có `net11.0`. ([4.157.0](https://api.nuget.org/v3-flatcontainer/skiasharp.views.maui.controls/4.157.0/skiasharp.views.maui.controls.4.157.0.nupkg), [4.159.0-rc.1](https://api.nuget.org/v3-flatcontainer/skiasharp.views.maui.controls/4.159.0-rc.1.26567.13/skiasharp.views.maui.controls.4.159.0-rc.1.26567.13.nupkg))
- **[Fact]** Nhánh `main` của SkiaSharp chỉ khai `MauiVersion` cho net10 (10.0.110) và net9 (9.0.120). ([SkiaSharp.Build.props](https://github.com/mono/SkiaSharp/blob/main/source/SkiaSharp.Build.props))
- **[Fact]** `Microsoft.Maui.Controls` và `Microsoft.Maui.Core` 11.0.10 có TFM `net11.0`, `net11.0-maccatalyst27.0`, `net11.0-windows10.0.19041`; `Microsoft.Maui.Core` phụ thuộc `Microsoft.WindowsAppSDK` 2.5.1, còn `SkiaSharp.Views.WinUI` 4.157.0 khai `Microsoft.WindowsAppSDK` 1.7.250909003. ([MAUI](https://api.nuget.org/v3-flatcontainer/microsoft.maui.core/11.0.10/microsoft.maui.core.11.0.10.nupkg), [SkiaSharp.Views.WinUI](https://api.nuget.org/v3-flatcontainer/skiasharp.views.winui/4.157.0/skiasharp.views.winui.4.157.0.nupkg))
- **[Fact]** `Microsoft.Maui.Graphics.Skia` 11.0.10 phụ thuộc `SkiaSharp` 4.150.1, và trên Windows thêm `SkiaSharp.Views.WinUI` 4.150.1: MAUI 11 tự dùng SkiaSharp 4.x. ([nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.maui.graphics.skia/11.0.10/microsoft.maui.graphics.skia.11.0.10.nupkg))
- **[Fact]** SkiaSharp đã sửa gói native cho Mac Catalyst thành `net10.0-maccatalyst26.0` và `net9.0-maccatalyst18.0` (bản 3.119.4 từng sai `26.2`). ([#4031](https://github.com/mono/SkiaSharp/issues/4031))
- **[Fact]** MAUI 11 dùng CoreCLR làm runtime mặc định trên mọi nền; Windows chạy qua WinUI 3 (Windows 10 1809 trở lên), macOS qua Mac Catalyst. .NET 11 hỗ trợ Mac Catalyst 18.2, macOS 15.2, Xcode 26.4. ([What's new](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11), [Supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms))
- **[Suy luận]** App `net11.0` nhiều khả năng restore được gói net10 (NuGet lấy asset tương thích thấp hơn) và NuGet sẽ chọn Windows App SDK 2.5.1. Chưa ai xác nhận hai điều này bằng build, nên prototype phải làm trước tiên.

### 2.2 SKCanvasView hay SKGLView

| Khía cạnh | `SKCanvasView` | `SKGLView` |
|---|---|---|
| Kiểu render | Phần mềm (CPU) | GPU |
| Windows | `SKXamlCanvas`: vẽ vào buffer của một `WriteableBitmap`, `Invalidate()` xếp việc vào `DispatcherQueue` với ưu tiên Normal | `SKSwapChainPanel`; không hỗ trợ thuộc tính `Background` |
| Mac Catalyst | `SkiaSharp.Views.iOS.SKCanvasView`: vẽ trong `Draw(CGRect)` rồi đổ sang CGContext | `SKMetalView` (Metal) |
| Issue đang mở | #4275, #2341, #2967, #3255 | #4277, #5450, #2958, #2803, #2871 |

- **[Fact]** Nội dung bảng lấy từ nguồn SkiaSharp tại tag `v4.157.0`: tài liệu của `SKCanvasView` ("software (CPU) rendering", Windows là `SKXamlCanvas`), handler Windows của `SKCanvasView` và của `SKGLView`, handler Mac Catalyst của `SKGLView`, `SKXamlCanvas.cs`, `SKCanvasView` của iOS. ([SKCanvasView.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views.Maui/SkiaSharp.Views.Maui.Controls/SKCanvasView.cs), [SKCanvasViewHandler.Windows.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views.Maui/SkiaSharp.Views.Maui.Core/Handlers/SKCanvasView/SKCanvasViewHandler.Windows.cs), [SKGLViewHandler.Windows.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views.Maui/SkiaSharp.Views.Maui.Core/Handlers/SKGLView/SKGLViewHandler.Windows.cs), [SKGLViewHandler.MacCatalyst.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views.Maui/SkiaSharp.Views.Maui.Core/Handlers/SKGLView/SKGLViewHandler.MacCatalyst.cs), [SKXamlCanvas.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views/SkiaSharp.Views.WinUI/SKXamlCanvas.cs), [iOS SKCanvasView.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/source/SkiaSharp.Views/SkiaSharp.Views/Platform/iOS/SKCanvasView.cs))
- **[Fact]** App WPF hiện dùng `skia:SKElement` (`src/PdfReaderApp/Controls/PdfViewerControl.xaml`), control vẽ vào `WriteableBitmap` `Pbgra32`, tức cũng là surface phần mềm. ([SKElement.cs](https://github.com/mono/SkiaSharp/blob/v3.119.4/source/SkiaSharp.Views/SkiaSharp.Views.WPF/SKElement.cs))
- **[Fact]** Năm 2023 `SKGLView` chưa có handler cho Windows và Mac trong MAUI (báo cáo "Handler not found", đóng là trùng #2050). Mã 4.157.0 đã có handler cho cả hai nền. ([#2403](https://github.com/mono/SkiaSharp/issues/2403))
- **[Fact]** Mọi API SkiaSharp đều đi qua một `SKCanvas`: `PdfPagePainter.Paint(SKCanvas, PaintFrame)` đã tách khỏi control. Việc đổi giữa `SKCanvasView` và `SKGLView` chỉ ảnh hưởng đến nơi nhận sự kiện paint.
- **[Suy luận]** Chọn `SKCanvasView` cho đầu MAUI đầu tiên: đúng mô hình app đang có, ít rủi ro trên WinUI 3. Giữ `PdfPagePainter` nguyên và chỉ viết lại lớp control, để nếu cần thì thử `SKGLView` như một phép đo A/B.
- **[Suy luận]** Với bề mặt cuộn lớn, không view nào nên có cỡ bằng tài liệu. Giữ `SKCanvasView` cỡ viewport (như `SKElement` với `Margin="10"` hôm nay), cuộn ảo bằng offset như `VirtualCanvas` đang làm.

### 2.3 Vấn đề hiệu năng đã biết

- **[Fact]** #4275 (mở, 29/6/2026, SkiaSharp 4.148.0): `SKXamlCanvas` trên WinUI trễ một frame, nền nháy khi đổi tab; nguyên nhân nghi là `Invalidate()` xếp render vào `DispatcherQueue`. ([#4275](https://github.com/mono/SkiaSharp/issues/4275))
- **[Fact]** #4277 (mở, 4.148.0): `SKSwapChainPanel` trên WinUI sập `0xc0000409` khi đổi cỡ cửa sổ liên tục rồi đóng ngay; người báo cáo ghi có `DrawInBackground="true"`. ([#4277](https://github.com/mono/SkiaSharp/issues/4277))
- **[Fact]** #5450 (mở, 27/10/2026, 4.153.1): `SKSwapChainPanel` sập Control Flow Guard trong `libSkiaSharp.dll` trên WinUI 3 sau khi tạo lại panel; đổi sang `SKXamlCanvas` thì hết, 3.119.4 không sập. ([#5450](https://github.com/mono/SkiaSharp/issues/5450))
- **[Fact]** Còn mở: #2958 (`SKSwapChainPanel` sập trên .NET 8 WinUI 3), #2803 (`AccessViolationException`), #2871 (`SKGLView` không trong suốt trên MAUI và WinUI 3), #2341 (trễ scale khi đổi cỡ cửa sổ), #2967 và #3255 (vòng đời Unloaded của `SKXamlCanvas`). ([#2958](https://github.com/mono/SkiaSharp/issues/2958), [#2803](https://github.com/mono/SkiaSharp/issues/2803), [#2871](https://github.com/mono/SkiaSharp/issues/2871), [#2341](https://github.com/mono/SkiaSharp/issues/2341), [#2967](https://github.com/mono/SkiaSharp/issues/2967), [#3255](https://github.com/mono/SkiaSharp/issues/3255))
- **[Fact]** #3168 đã đóng (22/8/2025): `SKXamlCanvas` sập trên máy không có GPU; người bảo trì đưa cách tạm là cài VC++ runtime. ([#3168](https://github.com/mono/SkiaSharp/issues/3168))
- **[Fact]** Mac Catalyst: không tìm thấy issue hiệu năng riêng trên mono/SkiaSharp lẫn dotnet/maui với các truy vấn đã thử. #4891 chỉ nói về iOS (OpenGL ES); Mac Catalyst đã dùng `SKMetalView`. Đây là "không thấy", chưa phải "không có".
- **[Fact]** Trên dotnet/maui, #23039 báo `SKBitmap` cộng `DrawBitmap` chậm, đề nghị `SKImage` cộng `DrawImage`; issue đóng "not planned" 12/10/2026. ([dotnet/maui #23039](https://github.com/dotnet/maui/issues/23039))
- **[Suy luận]** Nếu sau này chuyển sang `SKGLView`, cần cache `SKImage` theo trang để texture không bị upload lại mỗi frame; ưu tiên thấp với `SKCanvasView`.

### 2.4 API SkiaSharp 4 đã khác 3.119.4 ở chỗ app chạm

- **[Fact]** `SKCanvas.DrawBitmap(bitmap, dest, paint)` và `DrawBitmap(bitmap, source, dest, paint)` là obsolete trong 4.x ("Use the overload with SKSamplingOptions instead"); mỗi lời gọi tạo một `SKImage.FromBitmap` tạm rồi `DrawImage`. `PdfPagePainter` dùng các overload này ở dòng 55, 60, 76. ([SKCanvas.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/binding/SkiaSharp/SKCanvas.cs))
- **[Fact]** Từ 4.148.0 nên dùng `SKPathBuilder` thay cho các mutator của `SKPath` (còn đó nhưng obsolete, chỉ cảnh báo); `SKPaint` không còn giữ trạng thái chữ. `PdfPagePainter` dùng `path.AddRect` ở dòng 144. ([4.148.0](https://mono.github.io/SkiaSharp/docs/releases/4.148.0.html))

Nguồn: [SkiaSharp.Views.Maui.Controls trên NuGet](https://www.nuget.org/packages/SkiaSharp.Views.Maui.Controls/4.157.0), [SkiaSharp.Views.Maui.Core trên NuGet](https://www.nuget.org/packages/SkiaSharp.Views.Maui.Core/4.157.0), [mono/SkiaSharp](https://github.com/mono/SkiaSharp), [What's new in .NET MAUI for .NET 11](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11)

## 3. Bỏ System.Drawing trong RenderEngine

### 3.1 Hiện tại

- **[Fact]** `RenderEngine.RenderPage` gọi `page.Render(width, height, dpi, dpi, PdfRotation.Rotate0, PdfRenderFlags.Annotations)` được `System.Drawing.Bitmap`, rồi `ToSKBitmap`: `LockBits` với `Format32bppPArgb`, `InstallPixels(SKImageInfo(w, h, Bgra8888, Premul), Scan0, Stride)`, `skBitmap.Copy()`. (`src/PdfReaderApp/Core/RenderEngine.cs`)
- **[Suy luận]** Đường đi hiện tại chạm buffer ba lần: PDFium ghi vào bitmap GDI+ (tạo với `Format32bppArgb`), GDI+ đổi sang `Format32bppPArgb` khi `LockBits`, Skia copy thêm một lần bằng `Copy()`.
- **[Fact]** Bên dưới, `PdfPage.Render` của PdfiumViewer.Net tạo `Bitmap(w, h, Format32bppArgb)`, `LockBits`, `FPDFBitmap_CreateEx(w, h, 4, Scan0, w * 4)`, `FPDFBitmap_FillRect` nền trắng `0xFFFFFFFF` (khi không có cờ `Transparent`), `FPDF_RenderPageBitmap`, rồi `FPDF_FFLDraw` vì app truyền cờ `Annotations`. Tham số `dpi` chỉ đặt resolution của bitmap, không đổi số pixel (vì app không truyền cờ `CorrectFromDpi`). ([PdfPage.cs](https://github.com/vrjure/PdfiumViewer/blob/master/src/PdfiumViewer/Core/PdfPage.cs))
- **[Fact]** `LibraryService.RenderThumbnail` là nơi gọi thứ hai: lấy `SKBitmap`, `SKImage.FromBitmap`, `Encode(Png, 85)`, ghi file. (`src/PdfReaderApp/Services/LibraryService.cs`)

### 3.2 Cách thay

```csharp
// PdfPage.Handle là FPDF_PAGE; chữ ký các hàm native xem fpdfview.h
public SKBitmap RenderPage(PdfPage page, float scale)
{
    int width = (int)(page.Width * scale), height = (int)(page.Height * scale);
    var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
    IntPtr fp = FPDFBitmap_CreateEx(width, height, FPDFBitmap_BGRA, bitmap.GetPixels(), bitmap.RowBytes);
    try
    {
        FPDFBitmap_FillRect(fp, 0, 0, width, height, 0xFFFFFFFF);   // nền trắng đục, như PdfiumViewer.Net
        FPDF_RenderPageBitmap(fp, page.Handle, 0, 0, width, height, 0, FPDF_ANNOT);
    }
    finally { FPDFBitmap_Destroy(fp); }
    return bitmap;   // không cần LockBits, không cần Copy()
}
```

- **[Fact]** `FPDFBitmap_CreateEx` nhận buffer ngoài (`first_scan`, `stride`); buffer ngoài thì người gọi tự giải phóng, `FPDFBitmap_Destroy` không đụng tới. SkiaSharp 4.157.0 có constructor `SKBitmap(int width, int height, SKColorType colorType, SKAlphaType alphaType)`, `GetPixels()`, `RowBytes`. ([fpdfview.h](https://pdfium.googlesource.com/pdfium/), [SKBitmap.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/binding/SkiaSharp/SKBitmap.cs))
- **[Fact]** PDFtoImage làm đúng cách này: `new SKBitmap(w, h, Bgra8888, Premul)`, truyền `bitmap.GetPixels()` và `bitmap.RowBytes` vào `FPDFBitmap_CreateEx`, `FillRect` nền, render, `Destroy`. ([PdfDocument.cs](https://github.com/sungaila/PDFtoImage/blob/master/src/PDFtoImage/Internals/PdfDocument.cs))
- **[Suy luận]** Bản 160 của header ghi `FPDFBitmap_Create` "doesn't initialize the buffer", nên bước `FPDFBitmap_FillRect` là bắt buộc để có nền trắng.
- **[Suy luận]** Bắt đầu với cờ `FPDF_ANNOT` là đủ theo mô tả header (render mọi annotation trừ widget và popup). Chỉ thêm `FPDF_FFLDraw` khi so ảnh với bản WPF thấy form thiếu.

### 3.3 Thứ tự byte màu và premultiplied alpha

- **[Fact]** PDFium: bitmap luôn 4 byte một pixel, "byte order is BGRx ... or BGRA" (xanh dương, xanh lá, đỏ, alpha), dòng trên trước, không khe giữa các dòng. ([fpdfview.h](https://pdfium.googlesource.com/pdfium/))
- **[Fact]** `FPDFBitmap_BGRA` có "Pixel components are independent of alpha" (straight alpha); `FPDFBitmap_BGRA_Premul` chỉ là thử nghiệm và chỉ hỗ trợ với renderer Skia. ([fpdfview.h](https://pdfium.googlesource.com/pdfium/))
- **[Fact]** Skia mặc định RGBA ở mọi nền, trừ BGRA trên Windows; tài liệu SkiaSharp ghi `PlatformColorType` trên Windows là `Bgra8888`, trên macOS và Linux là `Rgba8888`. Vì vậy phải khai `SKColorType.Bgra8888` tường minh như RenderEngine đang làm. ([SkTypes.h](https://github.com/google/skia/blob/main/include/core/SkTypes.h), [SKImageInfo.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/binding/SkiaSharp/SKImageInfo.cs))
- **[Suy luận]** GDI+ `Format32bppPArgb` trên máy little-endian cũng theo thứ tự B,G,R,A: `RenderEngine` hiện tại giả định vậy khi bọc buffer GDI+ thành `Bgra8888` và ảnh ra đúng. Vậy thứ tự byte không đổi khi bỏ bước trung gian.
- **[Fact]** Với alpha: PDFtoImage chỉ premultiply tay khi màu nền không đục; với nền đục thì straight và premultiplied cho cùng giá trị. ([PdfDocument.cs](https://github.com/sungaila/PDFtoImage/blob/master/src/PDFtoImage/Internals/PdfDocument.cs))
- **[Suy luận]** App không dùng cờ `Transparent`, nền luôn trắng đục, nên bọc kết quả là `SKAlphaType.Premul` là đúng và không cần premultiply. Nếu sau này muốn nền trong suốt (ví dụ dark mode) thì phải premultiply tay như PDFtoImage.
- **[Suy luận]** Truyền `bitmap.RowBytes` thay cho `width * 4` để không phụ thuộc cách Skia căn hàng.

Nguồn: [fpdfview.h của PDFium](https://pdfium.googlesource.com/pdfium/) (nội dung kiểm ở `build/native/include/pdfium/` của [bblanchon.PDFium.Win32 160.0.8179](https://www.nuget.org/packages/bblanchon.PDFium.Win32/160.0.8179)), [PDFtoImage PdfDocument.cs](https://github.com/sungaila/PDFtoImage/blob/master/src/PDFtoImage/Internals/PdfDocument.cs), [SKBitmap.cs](https://github.com/mono/SkiaSharp/blob/v4.157.0/binding/SkiaSharp/SKBitmap.cs), [System.Drawing.Common only supported on Windows](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only)

## 4. Bảy file trong Core tham chiếu System.Drawing hoặc System.Windows

### 4.1 Kiểu đang dùng

| File | Kiểu đang dùng | Dùng để làm gì |
|---|---|---|
| `Core/SelChar.cs` | `Rect` | `Bounds` của một ký tự |
| `Core/SelectionResult.cs` | `IReadOnlyList<Rect>` | Rect theo dòng của vùng chọn |
| `Core/SlotHitTester.cs` | `Point`, `Rect(x, y, w, h)`, `Rect.Contains(Point)` | Tìm trang theo điểm hoặc offset cuộn; đổi điểm màn hình sang điểm PDF |
| `Core/TextSelectionResolver.cs` | `Rect` (`Left`, `Right`, `Top`, `Bottom`, `Width`, `Height`), `Rect.Union(a, b)`, `Point.X`, `Point.Y` | Gộp rect theo dòng; tìm ký tự gần nhất |
| `Core/PdfObjectManager.cs` | `Rect Bounds`, `Point`, `Rect.Contains(Point)` | Hit test GhostText; bounds lấy từ `PdfPage.GetTextBounds` |
| `Core/DesktopPrompts.cs` | `MessageBox`, `MessageBoxButton`, `MessageBoxImage`, `MessageBoxResult`, `Clipboard`, `COMException` | Xác nhận xoá Bản soạn; ghi clipboard |
| `Core/RenderEngine.cs` | `Bitmap`, `Rectangle`, `ImageLockMode`, `PixelFormat.Format32bppPArgb` | Đổi ảnh GDI+ sang `SKBitmap` |

- **[Fact]** Không file nào trong bảy file dùng `Dispatcher`. `Dispatcher` và `DispatcherTimer` chỉ xuất hiện ở `Controls/` (3 file: `PdfViewerControl.xaml.cs`, `Viewer/ScrollSettleTimer.cs`, `CompositionView.xaml.cs`), `Services/WindowSnapshot.cs` và `ViewModels/` (7 file, qua `Application.Current?.Dispatcher` hoặc seam `Func<Dispatcher?>`).
- **[Fact]** Ngữ nghĩa cần giữ: WPF `Rect.Contains(Point)` tính cả bốn cạnh (`x >= _x && x - _width <= _x && ...`), trả false với rect rỗng; `Rect.Union(a, b)` là static và xử lý rect rỗng. `SlotHitTester` ghi rõ "biên tính là trúng". ([Rect.cs của WPF](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Rect.cs))

### 4.2 Kiểu và cơ chế thay thế

| Đang dùng | Đề xuất | Lý do |
|---|---|---|
| `Rect`, `Point`, `Size` của WPF | `readonly record struct` `Rect`, `Point`, `Size` (`double`) trong `PdfReaderApp.Core`, cùng kiểu với `RectF` và `PageSlot` đang có | Core phải build `net11.0` thuần trước khi có đầu MAUI; `PageSlot` và zoom là `double`; không kéo SkiaSharp vào Core (`SKRect` là `float`). Copy ngữ nghĩa `Contains` và `Union` của WPF |
| Lựa chọn khác | `Microsoft.Maui.Graphics.Rect`, `Point`, `Size` | Là `double`, đủ `Contains(Point)`, `Union(Rect, Rect)`; gói có TFM `net11.0` không phụ thuộc. Nhưng Core sẽ mang dependency mang tên MAUI, chưa cần ở chặng 1 ([API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.graphics.rect?view=net-maui-11.0)) |
| `MessageBox`, `Clipboard` | Chuyển `DesktopPrompts` ra lớp ứng dụng; Core chỉ nhận delegate `CopyToClipboard`, `ConfirmDelete` | `CompositionsViewModel` và `MainViewModel` đã nhận hai delegate này, chỉ cần hiện thực của đầu UI |
| `Dispatcher`, `DispatcherTimer` (không có trong bảy file) | Core không cần. Ở ViewModels, seam `Func<Dispatcher?>` của `OpenSetPersistence` và `ReadingSessionDependencies` thay bằng `IDispatcher` của MAUI hoặc `SynchronizationContext` | `IDispatcher` có `IsDispatchRequired`, `Dispatch`, `DispatchDelayed`, `CreateTimer`; mở rộng `DispatchAsync`, `StartTimer`, `GetSynchronizationContextAsync` ([IDispatcher](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.dispatching.idispatcher?view=net-maui-11.0)) |
| `System.Drawing` | Xem mục 3 | |

- **[Fact]** MAUI vẫn dùng `System.Windows.Input.ICommand`: `Command` của MAUI khai `using System.Windows.Input;` và `class Command : ICommand`. `IViewerHost.AddNoteFromSelectionCommand` không cần đổi. ([Command.cs](https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/Command.cs))

### 4.3 Các file khác cũng rò kiểu WPF (ngoài bảy file)

| File | Kiểu WPF hoặc PdfiumViewer đang lộ ra |
|---|---|
| `Controls/Viewer/PdfDocumentSession.cs` | `System.Windows.Size` qua `GetPageSize`; `PdfiumViewer.Core.PdfDocument`, `PdfPage` |
| `Controls/Viewer/PdfPagePainter.cs` | alias `WpfRect = System.Windows.Rect` (chỉ dùng `Left`, `Top`, `Width`, `Height`, `Bottom`) |
| `Controls/Viewer/ViewerFrameComposer.cs` | `System.Windows.Media.SolidColorBrush`, `Color`; `MaterialDesignThemeService` |
| `Controls/Viewer/TextSelectionController.cs`, `ViewerFrameState.cs`, `OverlayData.cs` | `Rect`, `Point` |
| `Controls/Viewer/ViewerPointerInput.cs`, `ViewerWheelInput.cs` | `UIElement`, `MouseButton`, `Key`, `MouseWheelEventArgs` ở phần đăng ký event; các method `Handle*` nhận giá trị thuần |
| `Controls/Viewer/IViewerHost.cs` | `Point`, `Size` |
| `Controls/Viewer/ScrollSettleTimer.cs` | `DispatcherTimer` |

### 4.4 Ảnh hưởng tới test trong `tests/PdfReaderApp.Tests`

- **[Fact]** 170 file test; 20 trong `Core`, 9 trong `Controls`; 14 file tham chiếu `System.Windows` hoặc `DispatcherTimer`. Project test hiện là `net10.0-windows` với `UseWPF`.
- **[Fact]** Chỉ đổi namespace của kiểu hình học là đủ (6 file): `Core/SlotHitTesterTests.cs`, `Core/TextSelectionResolverTests.cs`, `Controls/TextSelectionControllerTests.cs`, `Controls/PdfPagePainterTests.cs`, `Controls/ViewerPointerInputTests.cs`, `Controls/ViewerWheelInputTests.cs`. Chúng khởi tạo `Rect`, `Point` bằng số nguyên và đọc `ghost.Bounds.Left`, `Top`, `Width`, `Height`; riêng `ViewerPointerInputTests` còn dùng `MouseButton`, `Size` qua `IViewerHost` nên đi theo thay đổi của lớp input.
- **[Fact]** Phụ thuộc PDFium hoặc `RenderEngine` thật (5 file, là lưới an toàn khi thay engine): `Core/PdfDocumentLoadCharacterizationTests.cs` (gọi `PdfiumViewer.Core` trực tiếp; ghim 3 trang, trang đầu 595 x 842), `Controls/PdfDocumentSessionTests.cs` (`Dispose_ReleasesBackingStream`), `Controls/ViewerFrameComposerTests.cs`, `Controls/ViewerPointerInputTests.cs` (MapPage trên `scripts/assets/snapshot-sample.pdf`), `Services/LibraryServiceTests.cs` (thumbnail PNG).
- **[Fact]** Dispatcher (ngoài bảy file Core): `OpenSetPersistenceTests.cs` bơm `DispatcherFrame`; `ReadingSession/ReadingSessionFixture.cs` truyền `DispatcherProvider: () => null`. `DesktopPrompts` không có test trực tiếp.
- **[Suy luận]** `SlotHitTesterTests` có ca biên (`(110, 210)` trúng, `(105, 20)` hụt) nên sẽ bắt được nếu `Rect` mới quên ngữ nghĩa "biên tính là trúng". Quyết định "Dispose đóng stream" phải giữ trong lớp PDFium mới vì có test ghim.

Nguồn: mã nguồn trong repo (`src/PdfReaderApp/Core/`, `src/PdfReaderApp/Controls/Viewer/`, `tests/PdfReaderApp.Tests/`), [Rect.cs của WPF](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Rect.cs), [IDispatcher](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.dispatching.idispatcher?view=net-maui-11.0), [Microsoft.Maui.Graphics.Rect](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.graphics.rect?view=net-maui-11.0)

## 5. Việc phải kiểm trong prototype và ngăn xếp đề xuất

### 5.1 Chưa kiểm chứng, prototype phải đo

1. Restore, build và chạy `SkiaSharp.Views.Maui.Controls` 4.157.0 trên app `net11.0-windows10.0.19041` và `net11.0-maccatalyst` với MAUI 11.0.10 (Windows App SDK 1.7 so với 2.5.1; SkiaSharp chưa có TFM net11).
2. Extent lớn nhất mà `ScrollView` của MAUI chịu được trên WinUI 3 và Mac Catalyst, vì viewer hiện dựa vào `ScrollViewer` cộng một canvas cỡ nội dung (`InteractionCanvas.Width`, `Height`).
3. Độ trễ và độ mượt khi cuộn, zoom trên Mac Catalyst (`SKCanvasView` qua CGContext) và trên Windows (nhất là #4275).
4. So sánh pixel với bản WPF: widget của form (`FPDF_FFLDraw`), toạ độ chữ (binding mới trả point thật, lệch khoảng 0,05% so với hôm nay).
5. Vòng đời Unloaded rồi Loaded của `SKXamlCanvas` khi Tab bị Evict rồi Hydrate lại (#2967, #3255).
6. iText 8.0.5 trên macOS: không thuộc ticket này, chưa chạy thử.
7. Môi trường: máy dev này chỉ có SDK 10.0.203 với workload MAUI 10 (`maui-windows` 10.0.20, `maccatalyst` 26.2), chưa có .NET 11 SDK. Prototype trên Windows phải cài trước.

### 5.2 Ngăn xếp đề xuất

| Lớp | Hiện tại | Đề xuất cho MAUI | Tin cậy |
|---|---|---|---|
| Nạp, truy vấn PDF | `PdfiumViewer.Net.WPF` 3.0.4 | P/Invoke PDFium tự viết (`[LibraryImport]`, khoảng 25 hàm, một khoá chung) trên `bblanchon.PDFium.Win32` và `bblanchon.PDFium.iOS` 160.0.8179 (thêm `bblanchon.PDFium.macOS` cho test và công cụ `net11.0` trên macOS); tên thư viện `pdfium` trên Windows và macOS, `@rpath/pdfium.framework/pdfium` trên Mac Catalyst | Cao về khả năng; khối lượng tự viết là suy luận |
| Render trang | `PdfPage.Render` sang GDI+ rồi copy sang `SKBitmap` | `RenderEngine.RenderPage` render thẳng vào `SKBitmap(Bgra8888, Premul)` (mục 3.2) | Cao |
| Vẽ | `SkiaSharp.Views.WPF.SKElement` 3.119.4 | `SkiaSharp.Views.Maui.Controls.SKCanvasView` 4.157.0; `PdfPagePainter` giữ nguyên, đổi `DrawBitmap` sang overload có `SKSamplingOptions` | Trung bình (chưa build trên .NET 11) |
| Hình học lõi | `Rect`, `Point`, `Size` của WPF | Kiểu `double` tự viết trong Core | Cao |
| Hộp thoại, clipboard | `DesktopPrompts` (WPF) | Lớp ứng dụng, cắm qua delegate có sẵn | Cao |
| Dispatcher | `Dispatcher`, `DispatcherTimer` của WPF ở ViewModels và Controls | `IDispatcher` của MAUI hoặc `SynchronizationContext` | Trung bình (thuộc ticket ViewModels) |

Nguồn: tổng hợp từ các mục 1 đến 4.
