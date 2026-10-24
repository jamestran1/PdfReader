# Coding standards

Nguồn quy tắc code duy nhất cho agent viết code và AI review của Trí Thư. `CLAUDE.md` nạp file này, AI review đọc bản trên base branch.

Cách đọc:

- Mỗi quy tắc có mã nhóm (`UI-2`, `TEST-1`...), một dòng quy tắc, một dòng lý do và mức mặc định. Góp ý review trích mã (vd `R3 · UI-2`).
- Mức: 🔴 blocker, 🟡 should, 🔵 nit. Mức chỉ là mặc định; reviewer được nâng hoặc hạ nếu ghi lý do.
- **(máy kiểm)**: công cụ đã kiểm quy tắc này, AI review không nhắc lại.
- Quy tắc mới có hiệu lực từ PR sau khi file này được merge. Góp ý về code từ nay thêm vào đây qua PR, không lưu vào memory của agent.

## Analyzer

Style, format, đặt tên và độ phức tạp do analyzer kiểm **(máy kiểm)**, không chép lại ở đây. Xem `.editorconfig`, `Directory.Build.props` và `CodeMetricsConfig.txt`.

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

## TEST: kiểm thử

**TEST-1** 🔴 Mỗi bug fix kèm một test tái hiện lỗi: đỏ trước khi sửa, xanh sau khi sửa. Đặt tên test theo hành vi được bảo vệ.
Lý do: test pass mà không phủ đường bị lỗi thì không chứng minh được bản sửa và không chặn lỗi quay lại.

**TEST-2** 🟡 Đường bị lỗi chưa test được thì thêm seam (tách class thuần, method có thể gọi từ test) rồi mới viết test, không bỏ qua test.
Lý do: phần lớn lỗi khó nằm trong code-behind WPF, chỉ test được sau khi tách ra.

## ARCH: kiến trúc

**ARCH-1** 🟡 Thay đổi kiến trúc phải theo ADR trong `docs/adr/`; đi ngược hoặc mở rộng một quyết định thì viết ADR mới.
Lý do: ADR là chỗ duy nhất ghi lại đánh đổi đã chọn; đổi kiến trúc âm thầm làm các quyết định cũ mất nghĩa.

## REPO: những thứ thấy được trong diff

**REPO-1** 🔴 Không commit secret hay API key; đọc từ biến môi trường hoặc user-secrets. (máy kiểm: GitHub secret scanning + push protection)
Lý do: repo public, key lọt vào lịch sử git coi như đã lộ.

**REPO-2** 🔴 Không commit `conductor/`, `.serena/`, `obj/`, `bin/` hay file tạm `*_wpftmp.*`.
Lý do: đây là công cụ cục bộ và sản phẩm build, không phải mã nguồn.

## Bẫy đã biết

- **Viewer bị ẩn vẫn ghi `CurrentPage`.** Khi tab bị ẩn, `ScrollViewer` của `PdfViewerControl` về offset 0 và bắn `ScrollChanged`; handler tính ra trang 1 rồi ghi ngược qua binding TwoWay vào `OpenTab.Page`. Mọi đồng bộ cuộn sang trang phải đi qua `Core.ViewerScrollGate.ShouldSyncPageFromScroll`.
- **Key brush của MDT nằm trong BAML đã biên dịch.** Muốn biết key nào còn sống, tìm trong dll (`grep -a -oE 'MaterialDesign\.Brush\.[A-Za-z.]*' MaterialDesignThemes.Wpf.dll`). `MaterialDesign.Brush.OnPrimary` không tồn tại.
- **Lỗi giao diện không có seam test.** Agent không nhìn thấy GUI: khi lỗi hiển thị còn đó, xin ảnh chụp màn hình sớm và đo pixel thay vì đoán qua nhiều vòng.
