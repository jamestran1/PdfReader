# Coding standards

Nguồn quy tắc code duy nhất cho agent viết code và AI review của Trí Thư. `CLAUDE.md` nạp file này, AI review đọc bản trên base branch.

Cách đọc:

- Mỗi quy tắc có mã nhóm (`UI-2`, `TEST-1`...), một dòng quy tắc, một dòng lý do và mức mặc định. Góp ý review trích mã (vd `R3 · UI-2`).
- Mức: 🔴 blocker, 🟡 should, 🔵 nit. Mức chỉ là mặc định; reviewer được nâng hoặc hạ nếu ghi lý do.
- **(máy kiểm)**: công cụ đã kiểm quy tắc này, AI review không nhắc lại.
- Quy tắc mới có hiệu lực từ PR sau khi file này được merge. Góp ý về code từ nay thêm vào đây qua PR, không lưu vào memory của agent.

## Analyzer

Style, format, đặt tên và độ phức tạp do analyzer và `dotnet format` kiểm qua `scripts/check.ps1` **(máy kiểm)**, không chép lại ở đây. Xem `.editorconfig`, `Directory.Build.props` và `CodeMetricsConfig.txt`.

Nguồn tham khảo cho nhóm CS: [quy ước code C# của Microsoft](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) và một số bài về clean code C#. Phần máy kiểm được đã nằm trong analyzer; ở đây chỉ giữ phần cần người hoặc AI đánh giá.

## UI: giao diện và XAML

**UI-1** 🟡 Dùng component MaterialDesignThemes thật theo M3 (`DialogHost`, `Card`, `Snackbar`, `TextBox` với `HintAssist`, chip, icon `Button`), không tự vẽ đồ giả bằng `Border`/`Rectangle`.
Lý do: đồ tự vẽ lệch elevation, state layer và shape của M3, nhìn lạc khi đứng cạnh component thật.

**UI-2** 🟡 Nút dài (filled, text, outlined) là hình chữ nhật bo góc `CornerRadius="12"`, không bao giờ hình viên thuốc. Đọc `border-radius:999px` trong CSS thiết kế là "bo góc 12". Nút icon vuông, tròn được miễn.
Lý do: chủ repo đã sửa lỗi này nhiều lần; quyết định của chủ repo đứng trên bản thiết kế.

**UI-3** 🔴 Không dùng `PrimaryHueMidBrush` / `PrimaryHueMidForegroundBrush`; dùng `MaterialDesign.Brush.Primary`, `MaterialDesign.Brush.Primary.Foreground` hoặc `TriThu.Brush.*`. (máy kiểm trong XAML của diff)
Lý do: dưới `CustomColorTheme` các key cũ không tồn tại, `DynamicResource` âm thầm trả về trong suốt.

**UI-4** 🔴 Token màu mới phải thêm vào cả `TriThuTokens.xaml` lẫn `TriThuTokens.Dark.xaml` với cùng `x:Key`, bind bằng `DynamicResource`; không hardcode màu.
Lý do: thiếu một bên thì chế độ sáng hoặc tối hiển thị sai mà build không báo.

**UI-5** 🟡 Không dùng `CornerRadius="100"` để làm bo tròn hai đầu; nếu thật sự cần, dùng đúng nửa chiều cao. (máy kiểm trong XAML của diff)
Lý do: trong WPF 10 giá trị này không bị kẹp về nửa chiều cao mà vẽ thành hình elip trên phần tử không vuông.

**UI-6** 🟡 Dùng `Slider` có sẵn của MaterialDesignThemes, không tự làm template `Track`.
Lý do: `Track` tự làm lệch thumb theo chiều dọc; bản có sẵn tự căn giữa và đã theo màu theme.

**UI-7** 🔴 Chuỗi tiếng Việt (UI, tooltip, thông báo, comment) giữ đủ dấu, không viết thành ASCII.
Lý do: build và test không bắt được chữ mất dấu, và giao diện còn lại đều có dấu.

## CS: C# và MVVM

**CS-1** 🟡 ViewModel dùng CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`); logic nghiệp vụ nằm trong ViewModel hoặc class thuần, không trong code-behind.
Lý do: code-behind không có seam để test; logic tách ra class thuần mới có test xUnit.

**CS-2** 🔴 Tài nguyên Skia và PDFium (`SKBitmap`, `SKSurface`, `SKPaint`, document, page...) phải được `Dispose`, bằng `using` hoặc qua `IDisposable` của lớp sở hữu.
Lý do: bộ nhớ native không do GC quản lý; rò rỉ khi cuộn nhiều trang làm app phình và treo.

**CS-3** 🟡 `async void` chỉ dùng cho event handler; method async khác trả về `Task`.
Lý do: exception trong `async void` không bắt được và làm sập app.

**CS-4** 🟡 Chỉ bắt exception mà code xử lý được: dùng kiểu cụ thể hoặc `catch (Exception ex) when (...)`. `catch (Exception)` trần chỉ đặt ở ranh giới (event handler, `[RelayCommand]`, vòng lặp stream) và phải hiển thị cho người dùng, ghi log, hoặc bọc lại kèm inner exception; không nuốt im lặng. (máy kiểm một phần: CA1031)
Ở ranh giới hợp lệ, tắt CA1031 ngay tại chỗ và ghi lý do: `#pragma warning disable CA1031 // ranh giới UI: hiện lỗi cho người dùng` trước `catch` và `#pragma warning restore CA1031` sau khối đó, hoặc `[SuppressMessage("Design", "CA1031", Justification = "...")]` trên method. Không tắt CA1031 cho cả file hay cả project.
Khi chuyển code sang method hay class khác, mọi lời gọi từng nằm trong một `try` vẫn phải được cùng nhánh `catch` đó bắt; reviewer so với base branch, không chỉ đọc code mới.
Lý do: nuốt lỗi im lặng che lỗi thật và làm bug không tái hiện được. Còn chuyển một lời gọi I/O ra ngoài `try` thì làm mất fallback mà không test nào đỏ (#277: `GetDocuments` ra ngoài `try` của RAG).

**CS-5** 🔵 Chuỗi hoặc số mang nghĩa miền dùng ở nhiều nơi (vai trò chat, key setting, tên bảng) là `const` PascalCase hoặc `enum`, không lặp literal. Chuỗi UI và giá trị chỉ dùng một lần được miễn.
Lý do: sửa một chỗ mà quên chỗ khác thì build vẫn xanh, lỗi chỉ lộ khi chạy.

**CS-6** 🟡 Tên nói đúng việc làm và dùng thuật ngữ trong `GLOSSARY.md`; một khái niệm một từ (không xen kẽ `Load` / `Fetch` / `Get` cho cùng một việc); tránh tên mơ hồ như `Handle`, `DoWork`, `Process`, `data`, `x`.
Lý do: analyzer chỉ kiểm cách viết hoa; tên lệch glossary làm người và agent hiểu sai mô hình miền.

**CS-7** 🔵 Comment giải thích "vì sao" (ràng buộc, bẫy, issue dẫn tới quyết định), không kể lại code đang làm gì, không ghi nhãn tiến độ (`Tab S1`, `SP2 Task 8`). Xoá code đã comment-out.
Lý do: nhãn tiến độ mất nghĩa sau khi merge, còn lịch sử đã có git giữ.

**CS-8** 🟡 Mỗi method ở một mức trừu tượng: không trộn tính toán thuần với I/O, gọi PDFium/Skia hay cập nhật UI trong cùng thân hàm; tách phần tính toán ra method hoặc class thuần.
Lý do: phần tính toán tách riêng mới test được (xem TEST-2); MA0051 chỉ đo độ dài, không đo việc trộn tầng.

**CS-9** 🔵 Method có hơn 3 tham số, hoặc nhiều tham số cùng kiểu dễ đổi chỗ, thì gom thành `record`. Không dùng tham số `bool` để rẽ hai hành vi khác nhau; tách thành hai method.
Lý do: truyền sai thứ tự hai tham số cùng kiểu thì compiler không bắt.

**CS-10** 🔵 Model, DTO và kết quả trả về ưu tiên bất biến (`record`, `init`, chỉ `get`); collection lộ ra ngoài dùng `IReadOnlyList<T>` hoặc `IReadOnlyCollection<T>`. State của ViewModel dùng `[ObservableProperty]` được miễn.
Lý do: state chỉ đổi qua một đường thì dễ lần theo khi có lỗi đồng bộ.

**CS-11** 🟡 Không dùng state `static` có thể thay đổi hay singleton tĩnh. Phụ thuộc (settings, file, PDFium, LLM, database) đi qua constructor dưới dạng interface.
Lý do: state tĩnh rò giữa các test và không có seam để thay bằng fake.

**CS-12** 🔵 Callback dùng ở một chỗ thì dùng `Func<>` / `Action<>`, và tên biến hoặc tham số nói mục đích. Chữ ký mang nghĩa miền hoặc dùng ở nhiều nơi thì dùng interface hoặc delegate có tên. Lambda dài hơn vài dòng thì tách thành method có tên.
Lý do: `Func<string, int, bool>` không cho biết tham số nào là gì; lambda dài khó đặt breakpoint và khó test.

**CS-13** 🟡 Tác vụ async chạy lâu do người dùng khởi động (lập chỉ mục, gọi AI, tìm kiếm, render) nhận `CancellationToken` và bị huỷ khi đóng tab, đổi tài liệu hoặc gửi yêu cầu mới. Method async đặt tên có hậu tố `Async` (máy kiểm: MA0137, trừ `tests/`).
Lý do: tác vụ cũ chạy tiếp sẽ ghi kết quả vào sai tài liệu; CA2016 chỉ kiểm việc chuyển tiếp token, không kiểm có huỷ hay không.

## TEST: kiểm thử

**TEST-1** 🔴 Mỗi bug fix kèm một test tái hiện lỗi: đỏ trước khi sửa, xanh sau khi sửa. Đặt tên test theo hành vi được bảo vệ.
Lý do: test pass mà không phủ đường bị lỗi thì không chứng minh được bản sửa và không chặn lỗi quay lại.

**TEST-2** 🟡 Đường bị lỗi chưa test được thì thêm seam (tách class thuần, method có thể gọi từ test) rồi mới viết test, không bỏ qua test.
Lý do: phần lớn lỗi khó nằm trong code-behind WPF, chỉ test được sau khi tách ra.

## ARCH: kiến trúc

**ARCH-1** 🟡 Thay đổi kiến trúc phải theo ADR trong `docs/adr/`; đi ngược hoặc mở rộng một quyết định thì viết ADR mới.
Lý do: ADR là chỗ duy nhất ghi lại đánh đổi đã chọn; đổi kiến trúc âm thầm làm các quyết định cũ mất nghĩa.

**ARCH-2** 🟡 ADR mở rộng hoặc tạo ngoại lệ cho một ADR cũ thì ghi ở Consequences: (1) những hành vi code hiện có bị đổi, kèm tên class hoặc bảng (vd `OpenSetPersistence` bỏ qua Default Workspace); (2) nguyên văn những câu của ADR cũ bị lật hoặc thu hẹp. Class nêu ở Consequences phải khớp với Quyết định: không dùng lại class của một đường mà Quyết định đã loại.
Lý do: reviewer chỉ đọc diff tài liệu nên không thấy ADR mâu thuẫn với code hay với ADR cũ. Ở #284, bốn câu hỏi về Default Workspace chỉ lộ ra khi đối chiếu code; ở #285, ADR 0009 lật bốn câu của ADR 0003/0004 mà không nêu, và cho Default Workspace dùng lại `RestoreOpenSet` dù đã quyết không chuyển nó sang `TabSetViewModel`.

## REPO: những thứ thấy được trong diff

**REPO-1** 🔴 Không commit secret hay API key; đọc từ biến môi trường hoặc user-secrets. (máy kiểm: GitHub secret scanning + push protection)
Lý do: repo public, key lọt vào lịch sử git coi như đã lộ.

**REPO-2** 🔴 Không commit `conductor/`, `.serena/`, `obj/`, `bin/` hay file tạm `*_wpftmp.*`.
Lý do: đây là công cụ cục bộ và sản phẩm build, không phải mã nguồn.

**REPO-3** 🔵 Dòng Markdown thêm mới dùng dấu hai chấm, dấu phẩy, dấu chấm phẩy hoặc ngoặc đơn thay cho em dash (U+2014). (máy kiểm trên dòng diff)
Lý do: chủ repo chọn văn phong không em dash; dòng cũ còn em dash được giữ, chỉ dòng sửa mới bị chặn.

## Bẫy đã biết

- **Viewer bị ẩn vẫn ghi `CurrentPage`.** Khi tab bị ẩn, `ScrollViewer` của `PdfViewerControl` về offset 0 và bắn `ScrollChanged`; handler tính ra trang 1 rồi ghi ngược qua binding TwoWay vào `OpenTab.Page`. Mọi đồng bộ cuộn sang trang phải đi qua `Core.ViewerScrollGate.ShouldSyncPageFromScroll`.
- **Key brush của MDT nằm trong BAML đã biên dịch.** Muốn biết key nào còn sống, tìm trong dll (`grep -a -oE 'MaterialDesign\.Brush\.[A-Za-z.]*' MaterialDesignThemes.Wpf.dll`). `MaterialDesign.Brush.OnPrimary` không tồn tại.
- **Lỗi giao diện không có seam test.** Agent không nhìn thấy GUI: khi lỗi hiển thị còn đó, xin ảnh chụp màn hình sớm và đo pixel thay vì đoán qua nhiều vòng.
