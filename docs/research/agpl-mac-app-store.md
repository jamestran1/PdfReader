# AGPL-3.0 và Mac App Store: rủi ro và đường thoát (research cho #321)

Ngày: 2026-11-26. Ticket: #321, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Trí Thư cấp phép AGPL-3.0 vì dùng iText 8 (ADR 0006) và hiện phát hành qua Microsoft Store (ADR 0005). Chủ repo muốn bản macOS đi cả kênh tải trực tiếp lẫn Mac App Store. File này trả lời bốn câu hỏi của #321: (1) điều khoản nào của Apple và Microsoft chạm AGPL và tiền lệ ra sao, (2) nếu phải bỏ iText thì thay bằng gì và cỡ việc bao nhiêu, (3) bản tải trực tiếp có vướng AGPL không, (4) iText thương mại có giá và điều kiện thế nào. Đây là câu hỏi pháp lý nên file chỉ nêu điều khoản, lập trường của các bên, tiền lệ và mức rủi ro; không kết luận "hợp lệ" hay "vi phạm".

Quy ước: **[Fact]** là điều nguồn chính viết (đã mở ngày 2026-11-26); **[Suy luận]** là nhận định của tôi; "chưa kiểm chứng" là chỗ không tìm được nguồn chính. Dữ kiện về gói NuGet lấy từ chính file nupkg và assembly (soi bằng reflection trên NuGet cache; không build, không chạy test).

## Tóm tắt

- **[Fact]** Apple không có ngoại lệ cho phần mềm tự do. EULA tự viết "must include and may not be inconsistent with" Exhibit B (giấy phép không chuyển nhượng, theo Usage Rules); không có EULA thì Standard EULA áp dụng. Usage Rules cấm vượt qua "security technology". FSF coi đúng dạng hạn chế này là "further restrictions" mà AGPL §10 cấm.
- **[Fact]** Nhà phát triển phải bảo đảm với Apple (ADPLA §5.1) rằng license của app và của mọi FOSS "will be consistent with and not conflict with" ký số, chống sao chép, "or any of the terms, conditions or requirements of the Program or this Agreement"; phát hiện xung đột phải báo Apple; Apple "may immediately cease distribution". Trách nhiệm dồn về nhà phát triển.
- **[Fact]** Installation Information (AGPL §6) chỉ phát sinh khi convey phần mềm trong một "User Product" kèm giao dịch chuyển quyền sở hữu thiết bị. **[Suy luận]** App trên Store không thuộc trường hợp đó, và Mac không khoá chỉ chạy app có chữ ký của Apple. Điểm va chạm chính là §10 và §12 với EULA và Usage Rules, không phải Installation Information.
- **[Fact]** Tiền lệ: Apple gỡ GNU Go (2010) sau khi FSF khiếu nại xung đột với GPLv2, và gỡ VLC for iOS (01/2011); VLC quay lại 2013 nhờ viết lại và cấp phép kép MPLv2/GPLv2+ ("The MPLv2 is applicable for distribution on the App Store"). Ngược lại, đến 26/11/2026 nhiều app AGPL-3.0 vẫn trên iOS App Store (Signal, Zotero, Joplin, Immich, Element X) và app GPL vẫn trên Mac App Store (Bitwarden, Cyberduck), đều còn cập nhật. **[Suy luận]** Apple không gỡ theo license; hai lần gỡ có ghi nhận đều đi sau một khiếu nại.
- **[Fact]** Microsoft Store (App Developer Agreement 8.11) có carve-out rõ: quyền cấp cho Microsoft "except for any material subject to any FOSS licenses"; mục 4(c) đặt trách nhiệm tuân thủ FOSS (kèm yêu cầu mã nguồn) lên nhà phát triển. Điểm cần lưu ý duy nhất là 4(i): license tự viết phải "no more restrictive than" Microsoft Usage Rules.
- **[Suy luận]** AGPL §13 không đặt thêm nghĩa vụ lên Trí Thư. **[Fact]** Cơ sở: app chạy trên máy người dùng và gọi thẳng API OpenAI, Gemini, Claude bằng key của người dùng; repo không có endpoint nào của chủ repo; FSF FAQ đặt tiêu chí là có "reasonable expectation that a person will be interacting with the program remotely over a network" hay không.
- **[Fact]** Bản tải trực tiếp vẫn chịu AGPL §4, §5, §6 (kèm Corresponding Source đúng bản, giữ thông báo, kèm text license) nhưng không có Usage Rules, FairPlay hay ràng buộc EULA của Apple; cần Developer ID và notarization (Apple Developer Program 99 USD/năm, vẫn phải chấp nhận ADPLA).
- **[Fact]** `PdfiumViewer.Core` trong gói hiện dùng chỉ nhắm `net8.0-windows7.0`: có text theo ký tự, search nghĩa đen, Info dictionary; không có search regex, không có object và CTM của ảnh, không có XMP. PdfPig 0.1.16 (Apache-2.0) có chữ, ảnh (bounding box), Info và XMP, nhưng không có chiến lược định vị regex.
- **[Suy luận]** Cỡ việc khi bỏ iText: metadata nhỏ, vùng ảnh vừa, search rects là việc lớn (22 test đang ghim hành vi). Ước 1 đến 2 tuần cho search rects, 2 đến 4 ngày cho phần còn lại và fixture test. Rủi ro hành vi: thứ tự đọc, hình dạng hộp khớp, gốc toạ độ CropBox.
- **[Fact]** iText thương mại không công bố giá: hai gói (OEM Distribution License; Subscription License Volume tính theo số PDF xử lý mỗi năm), đều phải xin báo giá; mua thì "released from the AGPL requirements". **[Suy luận]** Không phù hợp app free.

## 1. Điều khoản Apple xung đột với AGPL, ngoại lệ, tiền lệ, Microsoft Store

### 1.1 Đính chính số điều khoản AGPL

Ticket ghi "Installation Information ở GPLv3 §10 và §11". Theo văn bản AGPL-3.0 thì:

| Điều khoản | Tên | Nội dung liên quan |
|---|---|---|
| §0 | Definitions | "Mere interaction with a user through a computer network, with no transfer of a copy, is not conveying." |
| §3 | Anti-Circumvention | Covered work không bị coi là "effective technological measure"; người convey từ bỏ quyền cấm vượt qua |
| §6 | Conveying Non-Source Forms | Định nghĩa "User Product" và "Installation Information"; nguồn phải "require no special password or key for unpacking, reading or copying" |
| §7 | Additional Terms | "All other non-permissive additional terms are considered 'further restrictions' within the meaning of section 10." |
| §10 | Automatic Licensing of Downstream Recipients | "You may not impose any further restrictions on the exercise of the rights granted or affirmed under this License." |
| §11 | Patents | Về sáng chế, không liên quan Installation Information |
| §12 | No Surrender of Others' Freedom | Không thoả mãn cùng lúc AGPL và nghĩa vụ khác thì "you may not convey it at all" |
| §13 | Remote Network Interaction | Bắt buộc cung cấp nguồn cho người dùng tương tác từ xa |

### 1.2 Điều khoản của Apple

ADPLA (bản công khai có Schedule 1 "last updated October 29, 2026"):

- **[Fact]** §1.2: "App Store exists for each of the following: iOS, iPadOS, macOS, tvOS, visionOS, and watchOS"; "Security Solution" là FairPlay, áp cho Licensed Application "to administer Apple's standard usage rules". Định nghĩa "FOSS" bao mọi phần mềm bắt buộc công bố nguồn hoặc cấp phép cho tác phẩm phái sinh, "including without limitation software distributed under the GNU General Public License". "Corresponding Product" là "other version of Your software application" (bản Windows và bản tải trực tiếp macOS cũng tính).
- **[Fact]** §3.3.4 A(v): app có FOSS thì phải tuân thủ license FOSS; không được dùng FOSS theo cách làm phần không-FOSS của Apple Software chịu điều khoản FOSS.
- **[Fact]** §5.1 (đoạn cuối): bảo đảm license của app, của "any third-party code or FOSS" không xung đột với ký số, chống sao chép, "or any of the terms, conditions or requirements of the Program or this Agreement"; không được "purport to require Apple ... to disclose or make available any of the keys ... related to the Security Solution, digital signing or digital rights management mechanisms".
- **[Fact]** Schedule 1 §3.2: EULA tự viết "must include and may not be inconsistent with" Exhibit B; không có thì dùng Standard EULA. Exhibit B §2: giấy phép "limited to a non-transferable license to use ... as permitted by the Usage Rules"; §10: Apple là bên hưởng lợi thứ ba, có quyền thực thi EULA.

Apple Media Services Terms and Conditions (Last Updated: September 14, 2026):

- **[Fact]** Usage Rules: chỉ dùng cho "personal, noncommercial purposes"; tối đa 10 thiết bị, trong đó tối đa 5 máy tính; "You may not tamper with or circumvent any security technology included with the Services or Content".
- **[Fact]** "Apple acts as an agent for App Providers" và "is a third-party beneficiary of the Standard EULA or Custom EULA ... and may therefore enforce such agreement".
- **[Fact]** Standard EULA a.: "nontransferable license"; "You may not transfer, redistribute or sublicense the Licensed Application"; không được modify hay tạo tác phẩm phái sinh.

App Store Review Guidelines:

- **[Fact]** Không có mục nào về open source hay GPL.
- **[Fact]** Mục chạm tới: 2.4.5(vi) app Mac "may not present a license screen at launch, require license keys, or implement their own copy protection"; 2.4.5(vii) "must use the Mac App Store to distribute updates"; 5.2.1 app do người hoặc tổ chức "that owns or has licensed the intellectual property" nộp.

### 1.3 So từng cặp và ngoại lệ cho phần mềm tự do

| AGPL | Apple | Nhận định |
|---|---|---|
| §10 cấm further restrictions | Usage Rules và Standard EULA (không chuyển nhượng, không redistribute, giới hạn thiết bị); ADPLA Schedule 1 §3.2 và Exhibit B | FSF: Usage Rules "are exactly the kind of 'further restrictions' that are prohibited", và lập luận này "would apply to all versions of the GNU GPL and AGPL". **[Suy luận]** Trong các tài liệu đã đọc không thấy quan điểm công khai của Apple về AGPL; ADPLA chỉ đặt bảo đảm lên nhà phát triển |
| §3, §6 (Installation Information) | FairPlay; bảo đảm §5.1 | **[Fact]** §6 chỉ yêu cầu Installation Information khi convey "in, or with, or specifically for use in, a User Product" trong giao dịch chuyển quyền sở hữu thiết bị. **[Suy luận]** Bản app tải về không thuộc trường hợp này; người dùng tự build được và macOS cho cài app ngoài Store nên không cần key của Apple |
| §12 | Bảo đảm §5.1 | **[Suy luận]** Nhà phát triển tự đứng giữa hai bộ điều khoản: AGPL §12 chặn convey nếu không thoả cả hai, còn Apple "may immediately cease distribution" |

Ngoại lệ cho phần mềm tự do **[Fact]**: không có điều khoản nào miễn EULA hay Usage Rules. Apple chỉ nhắc FOSS ở ba chỗ (định nghĩa §1.2, §3.3.4 A(v), §5.1). Standard EULA có một câu nới lỏng: lệnh cấm reverse-engineer và modify không áp "to the extent as may be permitted by the licensing terms governing use of any open-sourced components included with the Licensed Application". **[Suy luận]** Câu này nói về "components" nằm trong app; Trí Thư cả app là AGPL nên có bao trùm cả app hay không là chưa rõ.

### 1.4 Tiền lệ

| App | License (nguồn chính) | Trạng thái trên Store (Apple Search API và trang app, 26/11/2026) |
|---|---|---|
| GNU Go (iOS) | GPLv2 | FSF khiếu năm 2010; "Apple have decided to remove GNU Go from the App Store" |
| VLC for iOS (id 650377962) | GPL ở bản bị gỡ (chưa kiểm chứng nguồn chính); nay "bi-licensed under the GPLv2 (or later) and the MPLv2" | VideoLAN 09/01/2011: bản do Applidium nộp "has been removed by Apple". 18/7/2013: "full re-write", "The MPLv2 is applicable for distribution on the App Store. We worked hard with all major copyright holders to not repeat history" |
| Signal (iOS, id 874139669) | AGPL-3.0 (LICENSE ở main là nguyên văn AGPL) | v8.38.1 ngày 25/11/2026 |
| Zotero (iOS, id 1513554812) | AGPLv3 (COPYING của zotero-ios) | v2.0.8 ngày 03/10/2026; chỉ "Designed for iPad". Không thấy Zotero desktop (AGPLv3) trên Mac App Store khi tra |
| Joplin (iOS, id 1315599797) | AGPL-3.0-or-later (LICENSE gốc; `packages/app-mobile` không có LICENSE riêng) | v13.7.8 ngày 20/11/2026; chỉ "Designed for iPad" |
| Immich (iOS, id 1613945652) | AGPL-3.0 | v3.4.2 ngày 19/11/2026 |
| Element X (iOS, id 1631335820) | AGPL-3.0 | v26.11.3 ngày 23/11/2026 |
| Bitwarden (Mac App Store, id 1352778147) | GPL v3.0 mặc định (LICENSE.txt tại tag desktop-v2023.10.0 và desktop-v2024.2.0) | trên Store từ 28/02/2018; v2026.11.0 ngày 19/11/2026 |
| Cyberduck (Mac App Store, id 409222199) | GPL "either version 2 of the License, or (at your option) any later version" | trên Store từ 01/03/2011; v9.6.0 ngày 06/11/2026 |

- **[Fact]** FSF ghi rõ "We have not sued Apple", và Apple chọn gỡ app thay vì sửa điều khoản (2010).
- **[Suy luận]** Trong các app đã tra, chưa thấy app AGPL-3.0 bản native trên Mac App Store (không phải kết luận "không tồn tại").
- **[Fact]** Tra "zotero" và "joplin" ở danh mục Mac App Store: không có kết quả nào của Zotero hay Joplin.
- **[Suy luận]** Rủi ro thực tế nằm ở khiếu nại của người giữ bản quyền. Với Trí Thư, bên có thể khiếu nại là iText (chủ bản quyền thành phần AGPL) và chính chủ repo. Lý do để iText khiếu nại chỉ xuất hiện khi Trí Thư không giữ đúng AGPL (ví dụ không kèm nguồn).

### 1.5 Microsoft Store (kênh hiện tại)

- **[Fact]** App Developer Agreement bản 8.11 (Publish Date 17/3/2026, Effective 17/4/2026). §1(n) định nghĩa FOSS theo danh sách license được OSI chấp nhận. §4(b): quyền cấp cho Microsoft "except for any material subject to any FOSS licenses". §4(c) "FOSS Licenses": nhà phát triển chịu trách nhiệm "(i) compliance with those license terms and conditions including any source code availability requirements, and (ii) ensuring that Microsoft ... has the necessary rights". §5(a): app chứa FOSS vẫn phải tuân thủ Agreement.
- **[Fact]** §4(i): license tự viết "must, at a minimum, provide the Customer with ... usage rights that are no more restrictive than ... the Microsoft Usage Rules"; không có thì Standard Application License Terms áp dụng. §3(k): Microsoft "may remove or suspend the availability of any App ... for any reason", gồm khiếu nại xâm phạm sở hữu trí tuệ.
- **[Fact]** Đã đọc toàn văn 52 trang bản 8.11: không còn thuật ngữ "Excluded License". Lịch sử thay đổi ghi mục 4(c) được thêm ở bản 8.7 (2021). Store Policies (cập nhật 15/9/2026) không có mục nào về copyleft.
- **[Suy luận]** Cấu trúc rủi ro giống Apple (license tự viết không được hạn chế hơn Usage Rules), nhưng Microsoft tách rõ phần FOSS khỏi quyền được cấp và không có DRM kiểu FairPlay. Kênh hiện tại không thêm rủi ro mới.

### 1.6 Mức rủi ro và các lựa chọn

| Lựa chọn | Dữ kiện | Rủi ro (**[Suy luận]**) |
|---|---|---|
| A. Giữ AGPL, lên Mac App Store | Tiền lệ 15 năm: app GPL và AGPL tồn tại lâu dài trên cả hai Store; hai lần gỡ có ghi nhận đều đi sau một khiếu nại | Thấp về khả năng bị gỡ, nhưng nhà phát triển phải ký bảo đảm §5.1 trong khi Usage Rules và AGPL §10 kéo nhau theo lập trường của FSF; nếu bị gỡ thì mất kênh |
| B. Bỏ iText trước, đổi license, rồi lên Mac App Store | Thoát hẳn AGPL cho các bản phát hành sau (ADR 0006) | Chi phí kỹ thuật ở mục 2; các bản đã phát hành vẫn AGPL |
| C. macOS chỉ tải trực tiếp (notarized) | Không có Usage Rules, không FairPlay, không ràng buộc EULA của Store | Không vào được Mac App Store |

Nguồn: https://www.gnu.org/licenses/agpl-3.0.html , https://www.gnu.org/licenses/gpl-faq.html#AGPLv3InteractingRemotely , https://www.gnu.org/licenses/gpl-faq.html#GiveUpKeys , https://developer.apple.com/support/terms/apple-developer-program-license-agreement/ , https://www.apple.com/legal/internet-services/itunes/us/terms.html , https://developer.apple.com/app-store/review/guidelines/ , https://www.fsf.org/news/2010-05-app-store-compliance , https://www.fsf.org/blogs/licensing/more-about-the-app-store-gpl-enforcement , https://www.videolan.org/news.html#news-2011-01-09 , https://www.videolan.org/press/ios2out.html , https://github.com/videolan/vlc-ios/blob/master/COPYING , https://github.com/signalapp/Signal-iOS/blob/main/LICENSE , https://github.com/zotero/zotero-ios/blob/master/COPYING , https://github.com/laurent22/joplin/blob/dev/LICENSE , https://github.com/immich-app/immich/blob/main/LICENSE , https://github.com/element-hq/element-x-ios/blob/main/LICENSE , https://github.com/bitwarden/clients/blob/main/LICENSE.txt , https://cyberduck.io/license/ , https://itunes.apple.com/search?term=bitwarden&entity=macSoftware&country=us , https://learn.microsoft.com/en-us/legal/windows/agreements/app-developer-agreement , https://learn.microsoft.com/en-us/windows/apps/publish/store-policies

## 2. Bỏ iText: thay từng chỗ dùng bằng gì

### 2.1 API iText đang dùng

| API iText | Dùng ở đâu | Mục đích |
|---|---|---|
| `PdfReader`, `PdfDocument` (chỉ đọc), `GetNumberOfPages`, `GetPage` | cả ba file | mở file, đếm trang, lấy trang |
| `page.GetCropBox()` (`ITextPdfDocumentService`), `page.GetPageSize()` (`PdfImageLocator`) | hai file đó | gốc CropBox của hộp khớp; kích thước trang để chuẩn hoá vùng ảnh |
| `PdfTextExtractor.GetTextFromPage(page, new LocationTextExtractionStrategy())` | `ITextPdfDocumentService.ExtractPageTexts` | text nguyên trang cho RAG |
| `PdfCanvasProcessor(listener).ProcessPageContent`, `EventType.RENDER_TEXT`, `TextRenderInfo.GetText/GetBaseline/GetFontSize` | `ExtractStructure` | từng run chữ: x, y, rộng, cỡ chữ, loại cấu trúc |
| `RegexBasedLocationExtractionStrategy(pattern)`, `GetResultantLocations`, `GetRectangle`, `GetText` | `FindMatchRects`, `FindPassageRects`, `FindPassageHits` | hộp khớp theo regex không dấu, một hộp cho mỗi dòng |
| `EventType.RENDER_IMAGE`, `ImageRenderInfo.GetImageCtm`, `iText.Kernel.Geom.Matrix` | `PdfImageLocator` | CTM của ảnh, chỉ giữ ảnh song song trục |
| `PdfDocumentInfo.GetTitle/GetAuthor` | `PdfMetadataReader` | Info dictionary |
| `GetXmpMetadata`, `XMPMetaFactory.ParseFromBuffer`, `GetArrayItem(XMPConst.NS_DC, "publisher", 1)` | `PdfMetadataReader` | XMP `dc:publisher` |

**[Fact]** Không chỗ nào trong `src/` dùng iText để ghi hay sửa PDF.

### 2.2 Ứng viên

| Ứng viên | Phiên bản | License | API đang dùng nào đáp ứng được | Rủi ro | Nhận xét |
|---|---|---|---|---|---|
| PDFium qua API công cộng (P/Invoke; gói `bblanchon.PDFium.Win32`, `bblanchon.PDFium.macOS`) | gói hiện dùng 130.0.6721, đối chiếu header nhánh `chromium/6721`; gói macOS mới nhất 160.0.8179 | gói nhị phân Apache-2.0; PDFium BSD-3-Clause | Text: `FPDFText_GetText`, `FPDFText_GetCharOrigin`, `FPDFText_GetFontSize`, `FPDFText_GetFontInfo`. Hộp: `FPDFText_CountRects`, `FPDFText_GetRect`, `FPDFText_GetCharBox`, `FPDFText_GetLooseCharBox`. Ảnh: `FPDFPage_CountObjects`, `FPDFPage_GetObject`, `FPDFPageObj_GetType`, `FPDFPageObj_GetMatrix`, `FPDFPageObj_GetBounds`, `FPDFFormObj_CountObjects`, `FPDFFormObj_GetObject`. Info: `FPDF_GetMetaText`. Hộp trang: `FPDFPage_GetMediaBox`, `FPDFPage_GetCropBox`. Không có XMP, không có search regex (chỉ `FPDFText_FindStart` nghĩa đen) | Phải tự dựng hàng chữ rồi chạy .NET Regex. Header ghi matrix của object nằm trong Form "relative to the form that contains it" nên phải tự compose | Mạnh nhất cho hộp khớp và vùng ảnh vì app đã phụ thuộc PDFium |
| `PdfiumViewer.Core` (trong gói `PdfiumViewer.Net.WPF` 3.0.4, fork `vrjure/PdfiumViewer`) | 3.0.4 | Apache-2.0 | Có sẵn: `PdfPage.GetCountChars`, `GetText`, `GetTextBounds`, `GetCharIndexAtPos`, `Search(text, matchCase, wholeWord)`; `PdfDocument.GetInformation()` (Title, Author). Thiếu: regex, object và CTM của ảnh, XMP, cỡ chữ từng run. `PdfPage.Page` và `PdfPage.TextPage` là `IntPtr` công khai nên P/Invoke thêm được | Chỉ có `lib/net8.0-windows7.0`, phụ thuộc System.Drawing.Common và `bblanchon.PDFium.Win32`: không chạy trên macOS | Dùng được ngay trên Windows cho text nghĩa đen và Info; macOS phải có lớp gọi PDFium khác |
| PdfPig | 0.1.16 ổn định (22/8/2026); mới nhất là alpha 0.1.17 | Apache-2.0 | Text: `page.Letters`, `page.GetWords`, `ContentOrderTextExtractor.GetText`, reading order detector. Ảnh: `page.GetImages()` cho `IPdfImage.BoundingBox`. Info: `document.Information.Title`, `.Author`. XMP: `document.TryGetXmpMetadata` cho `XmpMetadata.GetXDocument()`. Không có định vị regex | README cảnh báo `page.Text` giữ "internal content order". `IPdfImage` không lộ CTM nên không lọc được ảnh xoay bằng `PdfImageRegion.IsAxisAligned` nếu không tự replay `page.Operations` | Tốt nhất cho RAG text và metadata (ứng viên duy nhất có XMP). Thuần C#, nhắm netstandard2.0 đến net9.0 |
| Docnet.Core | 2.6.0 (05/9/2023) | MIT | README chỉ nêu đọc text và render; không thấy API theo từ, regex, CTM ảnh, XMP | Bản ổn định cách đây 3 năm; không rõ hỗ trợ macOS | Không đủ |
| PDFiumSharp | 1.0.0 | theo file LICENSE trên GitHub | wrapper mỏng cho PDFium; README 11 dòng không mô tả API text hay object | netstandard1.4, ngày đăng NuGet bất thường, cần gói nhị phân riêng | Không đủ, ít được duy trì |
| gói NuGet `openpdf` | 1.6.2 | MIT | không liên quan | Không khai báo repository; mô tả là "pure C# PDF library", không phải OpenPDF (Java) | Loại |

**[Fact]** Gói viewer hiện tại `PdfiumViewer.Net.WPF` 3.0.4 chỉ nhắm `net8.0-windows7.0`, nên bản MAUI trên macOS dù thế nào cũng phải có lớp gọi PDFium mới. **[Suy luận]** Điều đó làm phương án "PDFium qua API công cộng" rẻ hơn so với nhìn lần đầu.

### 2.3 Rủi ro hành vi với search rects

Bộ test `tests/PdfReaderApp.Tests/Services/ITextPdfDocumentServiceTests.cs` (22 test) ghim các hành vi sau của iText. Mỗi dòng là một chỗ engine mới có thể lệch:

| Hành vi đang ghim | Vì sao dễ lệch |
|---|---|
| Cụm vắt hai dòng cho hai hộp, khác `PdfY` (`FindPassageRects_FragmentWrappingOntoNextLine_ReturnsOneRectPerLine`) | **[Suy luận]** iText gom chữ theo dòng trước khi khớp. Thứ tự ký tự của PDFium và PdfPig là thứ tự content stream; cụm vắt dòng hoặc xen header/footer chỉ khớp nếu engine mới tự nhóm dòng giống iText |
| Cột trái không tràn sang cột phải; footer và header chen giữa chunk không được tô | Cùng lý do thứ tự đọc; comment trong test ghi "iText xếp chữ theo dòng xuyên hai cột" |
| Chữ gạch nối cuối dòng `perfor-` rồi `mance` khớp cả hai nửa | PDFium chèn ký tự sinh sẵn (dòng mới, khoảng cách); `AccentInsensitiveRegex` dùng `\s*` và `\s+` nên chịu được, nhưng ánh xạ chỉ số ký tự phải tính cả ký tự sinh sẵn |
| Hộp tính từ gốc CropBox (`FindRects_PageWithCropBoxOffsetFromMediaBox_AreRelativeToCropBoxOrigin`) | Code đang trừ gốc CropBox vì iText trả toạ độ gốc MediaBox. Gốc toạ độ của `FPDFText_GetRect` so với CropBox là gì thì chưa đo: phải spike trên file CropBox lệch |
| `PassageRectMerger` coi hai hộp là cùng dòng khi cùng Y và Height | **[Fact]** Comment trong `PassageRectMerger.cs`: "iText trả cùng Y và Height cho các cụm trên một dòng". **[Suy luận]** `FPDFText_GetRect` cho hộp của từng đoạn chữ, `FPDFText_GetLooseCharBox` cho hộp theo cỡ chữ: chọn loại nào quyết định merger có còn đứng không |
| Ngưỡng tin câu trích (`SourcePassage.MinQuoteMatchRatio` = 0.5) và hiệu năng (#246: parse trang cỡ 1 ms, 4 cụm cỡ 16 ms, chạy trên UI thread) | Ngưỡng 0.5 nhiều khả năng được chỉnh trên kết quả của iText. Hiệu năng PDFium native chưa đo; PdfPig thuần C# càng chưa đo |

### 2.4 Cỡ việc (**[Suy luận]**)

| Việc | Quy mô mã | Ước lượng | Rủi ro |
|---|---|---|---|
| `PdfMetadataReader.cs` (53 dòng) | nhỏ | dưới 1 ngày | Thấp. PdfPig thay trọn, gồm `dc:publisher`. Với PDFium thì mất `Publisher` vì không có API XMP |
| `PdfImageLocator.cs` (89 dòng) | vừa | 1 đến 2 ngày | Trung bình. P/Invoke khoảng 6 hàm PDFium, thêm xử lý Form lồng nhau |
| `ITextPdfDocumentService.cs` (189 dòng) và 22 test (512 dòng) | lớn | 1 đến 2 tuần | Cao. Phải dựng chiến lược định vị regex mới và giữ nguyên hành vi đã ghim |
| Fixture test: 4 file test dùng iText `PdfWriter` để sinh PDF (`MainViewModelTests`, `LibraryServiceTests`, `PdfMetadataReaderTests`, `ITextPdfDocumentServiceTests`) | vừa | 1 đến 2 ngày | Thấp. Dùng `PdfDocumentBuilder` của PdfPig hoặc file PDF tĩnh |

Giữ nguyên được: `IPdfDocumentService`, `AccentInsensitiveRegex` (chỉ sinh pattern, không phụ thuộc iText), `SourcePassage`; `PassageRectMerger` có thể phải chỉnh dung sai.

Nguồn: https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/6721/public/fpdf_text.h , https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/6721/public/fpdf_edit.h , https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/6721/public/fpdf_transformpage.h , https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/6721/public/fpdf_doc.h , https://www.nuget.org/packages/PdfiumViewer.Net.WPF/3.0.4 , https://github.com/vrjure/PdfiumViewer , https://github.com/UglyToad/PdfPig , https://www.nuget.org/packages/PdfPig/0.1.16 , https://www.nuget.org/packages/Docnet.Core/2.6.0 , https://github.com/GowenGit/docnet , https://github.com/ArgusMagnus/PDFiumSharp , https://www.nuget.org/packages/openpdf/1.6.2 , https://www.nuget.org/packages/bblanchon.PDFium.macOS/130.0.6721

## 3. Bản tải trực tiếp (không qua Store) và AGPL

### 3.1 Điều khoản Network Use (§13)

- **[Fact]** AGPL §13: "if you modify the Program, your modified version must prominently offer all users interacting with it remotely through a computer network (if your version supports such interaction) an opportunity to receive the Corresponding Source".
- **[Fact]** FSF FAQ: câu hỏi cần đặt là "whether or not there is a reasonable expectation that a person will be interacting with the program remotely over a network"; app chạy qua SSH hay X từ xa "does not fall into this category".
- **[Fact]** Trong repo: `AiChatClientFactory` tạo client OpenAI, Gemini, Claude bằng `options.ApiKey` của người dùng; grep `https?://` trong `src/` chỉ thấy namespace XAML, không có endpoint nào của chủ repo.
- **[Suy luận]** Trí Thư là app desktop; người dùng không tương tác với Trí Thư từ xa, nên §13 không đặt thêm nghĩa vụ. Server của nhà cung cấp AI không chạy iText hay mã của Trí Thư. §13 chỉ xuất hiện nếu sau này chủ repo chạy server riêng có dùng mã AGPL (relay, đồng bộ).

### 3.2 Nghĩa vụ kèm mã nguồn và thông báo khi phát hành DMG hay PKG

- **[Fact]** §4: giữ thông báo bản quyền và không bảo hành, "give all recipients a copy of this License along with the Program". §5: tác phẩm dựa trên Program phải được cấp phép trọn vẹn theo AGPL và hiển thị "Appropriate Legal Notices". §6: object code phải kèm Corresponding Source.
- **[Fact]** §6(d): cho tải từ một nơi chỉ định và "offer equivalent access to the Corresponding Source in the same way through the same place at no further charge", kèm "clear directions next to the object code saying where to find the Corresponding Source"; người phân phối "remain obligated to ensure that it is available for as long as needed".
- **[Fact]** "Corresponding Source" gồm "scripts to control those activities" (build, cài đặt). FSF FAQ chấp nhận nguồn từ hệ quản trị phiên bản "as long as the source checkout process does not become burdensome", với "clear and convenient instructions for how to get the source for the exact object code they downloaded".
- **[Fact]** ADR 0006 và `docs/RELEASE.md` đã đi theo hướng này: tag `vX.Y.Z` là nguồn của từng bản, Store listing ghi license và link `https://github.com/jamestran1/PdfReader/tree/vX.Y.Z`.
- **[Suy luận]** Cách đó chuyển nguyên sang trang tải DMG/PKG: để link nguồn ngay cạnh nút tải. Với bản macOS, script đóng gói, entitlements, Info.plist và quy trình notarize cũng là "scripts to control those activities", nên phải nằm trong tag tương ứng.
- **[Suy luận]** `Appropriate Legal Notices`: AGPL §5(d) miễn nếu Program gốc không hiển thị, mà iText là thư viện không có giao diện. Một màn "Giấy phép" trong About là cách rẻ nhất để đáp điều này và không phạm Guidelines 2.4.5(vi) (cấm màn license lúc khởi động).

### 3.3 Khoảng trống hiện tại của repo (quan sát khi đọc)

- **[Fact]** `pack-msix.ps1` chỉ chạy `dotnet publish` rồi `winapp package`; không copy `LICENSE` hay `THIRD-PARTY-NOTICES.md`. `PdfReaderApp.csproj` không khai báo hai file này. Grep `AGPL` và `giấy phép` trong `src/` không thấy màn thông báo pháp lý nào.
- **[Fact]** `THIRD-PARTY-NOTICES.md` chưa liệt kê `Anthropic`, `Google.GenAI`, `Markdig` (cả ba có trong `PdfReaderApp.csproj`). Gói PdfiumViewer đang dùng là fork `vrjure/PdfiumViewer` trong khi notices trỏ về `pvginkel/PdfiumViewer` (cả hai Apache-2.0).
- **[Suy luận]** Chưa rõ gói phân phối có text license hay không; nên mở một gói thật để kiểm trước khi làm bản macOS. AGPL §6 yêu cầu object code được convey "under the terms of sections 4 and 5". Bản macOS nên copy `LICENSE` và notices vào bundle trong bước build.

### 3.4 Khác biệt khi không có DRM của Store

| | Mac App Store | Tải trực tiếp (DMG/PKG) |
|---|---|---|
| Ký số và DRM | FairPlay, chữ ký của Apple | Developer ID, không DRM |
| Điều khoản với người dùng | Usage Rules và EULA (ADPLA Schedule 1 §3.2, Exhibit B) | Chỉ license của app |
| Duyệt | App Review | Apple: "Notarization of macOS software is not App Review"; là "an automated system" |
| Bắt buộc với nhà phát triển | Apple Developer Program, chấp nhận ADPLA | Cũng cần Apple Developer Program (99 USD/năm) cho Developer ID; "all software built after June 1, 2019, and distributed with Developer ID must be notarized" |
| Người dùng thay bằng bản tự build | Không thể qua Store | Được; Gatekeeper vẫn chặn app chưa notarize cho tới khi người dùng cho phép |

- **[Fact]** ADPLA §10 (Indemnification) loại khỏi nghĩa vụ bồi thường "any Application for macOS that is distributed outside of the App Store and does not use any Apple Services or Certificates". **[Suy luận]** Bản notarized dùng Developer ID và notary service nên không nằm trong phần loại này.
- **[Fact]** Guidelines 2.4.5(vii): app trên Mac App Store phải cập nhật qua Store. **[Suy luận]** Hai kênh cùng lúc nghĩa là hai bản build phân biệt, không dùng chung cơ chế cập nhật.
- **[Fact]** iText yêu cầu khi dùng AGPL: "prominently mention iText and include the iText copyright and AGPL license in output file metadata, and also retain the producer line in every PDF that is created or manipulated using iText". **[Suy luận]** Trí Thư chỉ đọc PDF bằng iText nên nghĩa vụ này không phát sinh với file của người dùng.

Nguồn: https://www.gnu.org/licenses/agpl-3.0.html , https://www.gnu.org/licenses/gpl-faq.html#SourceInCVS , https://www.gnu.org/licenses/gpl-faq.html#AGPLv3ServerAsUser , https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution , https://developer.apple.com/programs/enroll/ , https://itextpdf.com/how-buy/AGPLv3-license , https://developer.apple.com/app-store/review/guidelines/

## 4. iText thương mại

- **[Fact]** iText dùng "dual-license model": commercial hoặc AGPL. `LICENSE.md` của `itext-dotnet`: "either version 3 of the License, or (at your option) any later version".
- **[Fact]** Phiên bản: repo đang dùng `itext` 8.0.5; mới nhất trên NuGet là 9.8.1.1 (17/11/2026). iText nay thuộc Apryse ("iText by Apryse").
- **[Fact]** Hai gói thương mại:
  - **OEM Distribution License**: "to embed iText into off-the-shelf software applications that you distribute to your customers"; "includes cases where you embed iText into end-user applications that are installed on your customers own devices"; "subscription-based and pricing will be calculated depending on your specific use case".
  - **Subscription License Volume** ("iText's standard commercial license"): giá "based on the specific volume of PDFs that your company processes ... annually"; support và maintenance "Included in standard subscription".
- **[Fact]** Không có giá công khai: bảng giá ghi "Custom" và các nút "Quote". Trang AGPL ghi điều kiện: "Disclosure of full source code of application needed, (including your own application)"; "It's a legal violation to use iText Core/Community and our open-source add-ons in a non-AGPL environment".
- **[Fact]** Nguồn thứ cấp (Vendr, nền tảng mua sắm phần mềm, không phải iText): "Avg Contract Value $44,891", tối đa "around $210,000". Đây là dữ liệu hợp đồng doanh nghiệp, không phải bảng giá.
- **[Suy luận]** Với Trí Thư, gói đúng là OEM Distribution License, phải xin báo giá, thuê bao theo năm. ADR 0006 đã loại phương án này và dữ kiện hiện tại không có gì để mở lại.

Nguồn: https://itextpdf.com/how-to-buy , https://itextpdf.com/how-buy/AGPLv3-license , https://itextpdf.com/how-buy/commercial/oem-distribution-license , https://itextpdf.com/how-buy/commercial/volume-based-license , https://github.com/itext/itext-dotnet/blob/develop/LICENSE.md , https://www.nuget.org/packages/itext/9.8.1.1 , https://www.vendr.com/buyer-guides/itextpdf

## Còn chưa kiểm chứng

- Nguyên nhân gỡ VLC for iOS năm 2011 (người khiếu nại, nội dung khiếu nại): chỉ đọc được thông báo của VideoLAN và thông cáo 2013.
- Gốc toạ độ của `FPDFText_GetRect` so với CropBox; hành vi ligature và ảnh trong Form lồng nhau của PDFium.
- Hiệu năng và độ chính xác của PDFium so với iText trên bộ tài liệu thật của Trí Thư.
- iText có từng phát biểu hay khiếu nại về việc app AGPL phân phối qua App Store hay không.
- Có app AGPL-3.0 bản native trên Mac App Store hay không: chỉ tra 10 tên, không có danh sách toàn cảnh.
- Đơn giá iText: chưa có số từ chính iText.
