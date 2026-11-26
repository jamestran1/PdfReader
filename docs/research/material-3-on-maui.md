# Material 3 trên MAUI 11: thay MaterialDesignThemes bằng gì (research cho #323)

Ngày: 2026-11-26. Ticket: #323, map cha: #315 "Migrate sang MAUI trên .NET 11: Windows và macOS".

Trí Thư (WPF, `net10.0-windows`) dùng `MaterialDesignThemes` 5.1.0 và `MaterialDesignColors` 5.3.2 ở 23 file (`src/PdfReaderApp/PdfReaderApp.csproj` dòng 29-30). Quyết định đã chốt là giữ Material Design 3 trên cả Windows (backend WinUI 3 của MAUI) và macOS (Mac Catalyst). Research này trả lời bốn câu hỏi: (1) `MaterialDesignThemes` có bản MAUI không, (2) ứng viên thay thế nào đúng M3, sáng tối theo token, chạy trên cả Windows và Mac Catalyst, tương thích .NET 11, (3) từng thành phần đang dùng thay bằng gì, (4) phần phải tự viết tốn bao nhiêu và rủi ro ở đâu. Mọi số phiên bản lấy trực tiếp từ NuGet, GitHub và docs Microsoft trong phiên làm việc ngày 26/11/2026. Ký hiệu: **[Fact]** là nguồn viết, **[Suy luận]** là nhận định của người research, "chưa kiểm chứng" là không tìm được nguồn.

## Tóm tắt

- **Không có bản MAUI chính thức của `MaterialDesignThemes`, cũng không có port đáng tin.** **[Fact]** Bản ổn định mới nhất trên NuGet là 5.4.0 (20/10/2026), chỉ nhắm `net10.0-windows7.0`, `net8.0-windows7.0`, `.NETFramework4.6.2`. Maintainer Keboo trả lời trong #2701: "At present there are no plans to bring this to Xamarin.Forms or MAUI". Thư viện vẫn bảo trì tích cực nhưng chỉ cho WPF.
- **MAUI 11 không có Material 3 dựng sẵn cho hai nền tảng đích.** **[Fact]** Docs Microsoft: Material 3 "is available on the Android platform"; `UseMaterial3` "has no effect on other platforms such as iOS, macOS, or Windows". Trên Windows và Mac Catalyst, MAUI dùng control native.
- **Không ứng viên cộng đồng nào thỏa đồng thời bốn điều kiện: đúng M3, gói `net11.0`, có Windows, có Mac Catalyst.** Ứng viên chín nhất là `UraniumUI.Material` 3.0.0: Apache-2.0, có TFM Windows và Mac Catalyst, repo đẩy code ngày 26/11/2026, nhưng chỉ nhắm `net10.0` và màu dính vào `StaticResource`.
- **Bốn ứng viên bị loại vì một lý do chí mạng mỗi cái.** `HorusStudio.Maui.MaterialDesignControls`: không có TFM Windows và phụ thuộc gói `Microsoft.Maui.Controls.Compatibility` (MAUI 11 ngừng ship gói này). `Material.Components.Maui`: dừng ở `0.2.2-preview` từ 02/2024, nhắm `net8.0`. `Shiny.Maui.Controls.Themes.Material`: mới ra 25/11/2026, không có TFM Mac Catalyst. `Flavor.Maui`: không có TFM Windows. **[Fact]**
- **`CommunityToolkit.Maui` 17.0.0** là một trong hai gói trong nhóm đã ra bản `net11.0` (10/11/2026, MIT; gói kia là Shiny nhưng thiếu Mac Catalyst), dùng cho popup có kết quả trả về và snackbar. Nó không phải bộ M3. **[Fact]**
- **Icon là điểm cần chú ý nhất.** **[Fact]** `PackIconKind` của MDT là bộ Pictogrammers MDI (tên như `BookOpenVariant`, `ViewSequential`); đối chiếu 40 Kind của Trí Thư với `meta.json` của MDI thì 39 khớp tên chính, `Settings` là alias của `cog`. Các gói icon MAUI phổ biến (`IconFont.Maui.MaterialDesignIcons`, `MauiIcons`, `UraniumUI.Icons`) là Google Material Icons hoặc Material Symbols, tên khác. Cách giữ nguyên tên Kind là tự làm một control icon nhỏ từ font MDI (license Apache 2.0) hoặc tái dùng file `PackIconKind.cs` sinh tự động của MDT (MIT).
- **`DialogHost`:** ba hướng thay: popup có kết quả của CommunityToolkit (`Popup<T>`, `ShowPopupAsync<T>` trả `IPopupResult<T>`), page modal, hoặc lớp overlay tự viết trong `Grid`. Với Trí Thư, overlay tự viết giữ nguyên được binding `IsDialogOpen` hai chiều. **[Suy luận]**
- **`HintAssist`:** MAUI chỉ có `Placeholder` (không nổi nhãn). Nhãn nổi kiểu outlined text field phải lấy từ `TextField`/`EditorField` của UraniumUI hoặc tự viết. **[Fact]** về `Placeholder`, **[Suy luận]** về phần còn lại.
- **`Card` và elevation:** thay bằng `Border` (bo góc qua `StrokeShape`) và `VisualElement.Shadow`, token `DropShadowEffect` đổi thành `Shadow`. **[Fact]** về API, **[Suy luận]** về độ tương đồng hình ảnh.
- **`MessageBox` ba chỗ:** thay bằng `Page.DisplayAlertAsync` (từ .NET 10 `DisplayAlert` bị deprecated, đổi tên thành `DisplayAlertAsync`). **[Fact]** Pop-up render bằng control native, không theo M3.
- **Chủ đề sáng tối chuyển được gần nguyên vẹn:** hai `ResourceDictionary` cùng key, thay lúc runtime, dùng `DynamicResource`, đúng quy trình docs MAUI. MAUI cũng có `AppThemeBinding`, `SetAppTheme<T>`, `Application.UserAppTheme`. **[Fact]**
- **Lỗ hổng phải tự lấp trên MAUI:** không có `ToolTip`, `Chip`, `Card`, `ToggleButton`, `GridSplitter`, `Popup` neo theo view trong danh sách control. **[Fact]** Repo Trí Thư có 55 chỗ `ToolTip`, 31 `ControlTemplate`, 57 `Trigger` (16 là `IsMouseOver`).
- **Đề xuất cho ticket prototype:** phương án A (token và control tự sở hữu trên MAUI thuần, chỉ thêm CommunityToolkit) là mặc định; đo thêm UraniumUI theo phương án B cho bốn điểm rủi ro: chạy trên `net11.0`, đổi theme lúc runtime, hover và focus trên Windows, dialog. **[Suy luận]**

## 0. Hiện trạng trong repo (số liệu để quy đổi)

Đếm bằng grep chỉ đọc trên `src/PdfReaderApp` ngày 26/11/2026 (có thể lệch nhẹ so với mô tả ticket do cách đếm):

| Thứ đang dùng | Số lượng | Ghi chú |
|---|---|---|
| File chứa chuỗi `MaterialDesign` | 23 (src), 6 (tests) | 3 converter và 3 file test dùng `PackIconKind`; `MarkdownPresenter` dùng `MaterialDesignThemes.Wpf` |
| Thẻ `<materialDesign:PackIcon` | 68 | Ticket ghi 73 chỗ |
| Giá trị `Kind` duy nhất | 34 trong XAML + 6 trong C# | Toàn bộ là tên Pictogrammers MDI |
| `Style x:Key` tự viết | 28 | 21 trong 7 file `Themes/*.xaml`, 7 trong `Controls/` |
| `ControlTemplate` / `Trigger` | 31 / 57 | 31 template trong 8 file; 57 trigger gồm 30 `DataTrigger`, 16 `Trigger IsMouseOver`, 4 `MultiDataTrigger`, 7 `Trigger` khác (`IsChecked`, `IsEnabled`, `IsKeyboardFocusWithin`, `IsSelected`) |
| `DynamicResource` | 208 | Trỏ vào `TriThu.Brush.*`, `TriThu.Elevation.*`, `MaterialDesign*` |
| `ToolTip` | 55 | Không có tương đương dựng sẵn trong MAUI |
| `HintAssist.Hint` / `HelperText` | 17 / 3 | `MainWindow.xaml`, `SettingsWindow.xaml`, `CompositionView.xaml`, `CreateCompositionDialogView.xaml` |
| `materialDesign:Card` / `materialDesign:Chip` | 7 / 1 | Chip ở `CompositionView.xaml` dòng 79 |
| `DialogHost` / `Popup` | 2 (lồng nhau) / 2 | `MainWindow.xaml` dòng 119, 125 và 319, 427 |
| `MessageBox.Show` | 3 | `App.xaml.cs:60`, `MainWindow.xaml.cs:26`, `Core/DesktopPrompts.cs:11` |

Token: `TriThuTokens.xaml` và `TriThuTokens.Dark.xaml` có cùng 32 key màu (`SolidColorBrush`), 6 cỡ chữ, 2 font và 3 `DropShadowEffect`. `MaterialDesignThemeService.Apply` thay dictionary trong `MergedDictionaries` rồi gọi `PaletteHelper.SetTheme`; canvas Skia đọc `MaterialDesignThemeService.Current` và đăng ký `ThemeChanged` (`Services/MaterialDesignThemeService.cs`).

## 1. `MaterialDesignThemes` có bản MAUI chính thức hoặc port đáng tin không?

**Không.** Bằng chứng:

- **[Fact]** NuGet `MaterialDesignThemes` có bản ổn định mới nhất 5.4.0 (đăng 20/10/2026), nhóm dependency chỉ có `net10.0-windows7.0`, `net8.0-windows7.0`, `.NETFramework4.6.2`. Không có TFM nào khác.
- **[Fact]** README repo tự mô tả: "Comprehensive and easy to use Material Design theme and control library for the Windows desktop (WPF)". README không nhắc MAUI.
- **[Fact]** Issue #2701 "How about MAUI?" (22/05/2022), Keboo (Member) trả lời cùng ngày: "At present there are no plans to bring this to Xamarin.Forms or MAUI. Much of this code is tied to WPF so it would be a major undertaking to do." Issue được chuyển thành discussion #2708, trạng thái Closed và Answered. Tìm discussion với từ "MAUI" chỉ ra đúng một kết quả này.
- **[Fact]** Issue #2299 "WinUI 3" mở từ 11/04/2021, vẫn open. Nghĩa là kể cả backend WinUI 3 của MAUI trên Windows cũng chưa được MDT hỗ trợ.
- **[Fact]** Danh sách repo công khai của org `MaterialDesignInXAML` gồm `MaterialDesignInXamlToolkit`, `Website`, `Displaying-XAML`, `MotionList`, `F1InXAML`; không có repo MAUI (đã xem trang 1).
- **[Fact]** Port cộng đồng gần nhất về mặt tên gọi là `yiszza/mdc-maui` (579 sao, MIT), là bộ control Material riêng cho MAUI chứ không port MDT; push cuối 18/05/2024.
- **[Suy luận]** Kết luận trong issue #2701 (2022) đến nay vẫn khớp với trạng thái NuGet và repo, nên có thể coi là tin cậy cao: không nên chờ MDT cho MAUI.

Nguồn: https://api.nuget.org/v3-flatcontainer/materialdesignthemes/index.json , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/master/README.md , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/issues/2701 , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/discussions/2708 , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/issues/2299 , https://github.com/orgs/MaterialDesignInXAML/repositories , https://github.com/yiszza/mdc-maui

## 2. Ứng viên cho MAUI 11

### 2.1 So sánh

| Ứng viên | Phiên bản hiện tại | .NET 11 | Windows | Mac Catalyst | License | Bảo trì | Nhận xét |
|---|---|---|---|---|---|---|---|
| MAUI dựng sẵn (`Microsoft.Maui.Controls`) | 11.0.10 (11/11/2026) | Có (`net11.0-*`) | Có (WinUI 3) | Có | MIT | Microsoft | Không có M3 cho Windows và macOS; có theming, `Border`, `Shadow`, `TitleBar`, pop-up native |
| `UraniumUI.Material` | 3.0.0 (14/06/2026) | Chưa: chỉ `net10.0-*` | Có | Có | Apache-2.0 | Push 26/11/2026; 1.488 sao, 154 issue mở | Ứng viên M3 chín nhất; màu là `StaticResource` |
| `HorusStudio.Maui.MaterialDesignControls` | 10.0.1 (12/02/2026) | Chưa; phụ thuộc gói Compatibility | Không có TFM (README: "upcoming") | Có | MIT | Push 25/11/2026; 85 sao | Loại: không chạy Windows trên MAUI 11 |
| `Material.Components.Maui` | 0.2.2-preview (11/02/2024) | Chưa: `net8.0-*` | Có | Có | MIT | Push cuối 18/05/2024 | Loại: ngừng gần 3 năm, vẫn preview |
| `Shiny.Maui.Controls.Themes.Material` | 3.0.0 (25/11/2026) | Có (`net11.0`) | Có | Không có TFM | MIT | Mới ra 1 ngày; 36 sao | Theme pack "Material Design 3 purple palette", chưa chín |
| `Material.MAUI` | 0.0.3 (22/09/2026) | Chưa: `net10.0-*` | Có | Có | MIT | 1 sao, chưa có release | Port của XF.Material, "Material 2/3 inspired"; quá sớm |
| `FlagstoneUI.Themes.Material` | 2.0.4 (21/06/2026) | Chưa: `net10.0-*` | Có | Có | MIT | 26 sao | 5 control trung tính, theme là ResourceDictionary; không phải bộ M3 đầy đủ |
| `Flavor.Maui` | 0.2.0 (17/04/2026) | Chưa: `net10.0-*` | Không có TFM | Có | MIT | Ít tín hiệu | Loại: không có Windows |
| `Syncfusion.Maui.Toolkit` | 1.0.12 (12/11/2026) | Chưa kiểm tra TFM | Có (theo README) | Có (theo README) | MIT (`LICENSE.txt` trong repo) | Push 26/11/2026; 752 sao | Bộ control riêng của Syncfusion, không phải hiện thực M3 |
| `CommunityToolkit.Maui` | 17.0.0 (10/11/2026) | Có (`net11.0-*`) | Có | Có | MIT | Push 25/11/2026; 2.710 sao | Không phải M3; có Popup, PopupPage, Snackbar, Toast |
| `Mopups` | 1.3.4 (31/05/2025) | Chưa: `net8.0-*` | Có | Có (README: "a bit iffy") | NuGet ghi MIT; GitHub nhận diện BSD-3-Clause | Push 28/02/2026; 90 issue mở | Popup riêng, chậm cập nhật |

### 2.2 MAUI dựng sẵn

- **[Fact]** Material 3 chỉ có trên Android và mặc định tắt: "In .NET MAUI, Material 3 design is available on the Android platform but is not enabled by default". Bật bằng thuộc tính build `UseMaterial3`.
- **[Fact]** "The `UseMaterial3` build property is specific to the Android platform. It has no effect on other platforms such as iOS, macOS, or Windows. On these platforms, .NET MAUI apps use the native design systems and controls specific to each platform."
- **[Fact]** "What's new" MAUI 11 chỉ thêm Material 3 cho handler Android của `ImageButton`, `DatePicker`, `Entry`, `Slider`. Không có control Material mới cho Windows hoặc Mac Catalyst.
- **[Fact]** Nền tảng: "Windows 11 and Windows 10 version 1809 or higher, using Windows UI Library (WinUI 3)"; "macOS 12 or higher, using Mac Catalyst". Toolchain .NET 11 đặt mức triển khai Mac Catalyst tối thiểu là 17.
- **[Fact]** Theming: quy trình bốn bước (định nghĩa theme bằng `ResourceDictionary`, đặt theme mặc định trong `App.xaml`, dùng `DynamicResource`, nạp theme lúc runtime). Theo theme hệ thống: `AppThemeBinding`, `SetAppThemeColor`, `SetAppTheme<T>`, `Application.UserAppTheme`, `Application.RequestedTheme`, sự kiện `RequestedThemeChanged`; hoạt động trên "iOS 13 or greater, Android 10, macOS 10.14 or greater, and Windows 10 or greater".
- **[Fact]** Danh sách control trên docs không có `ToolTip`, `Chip`, `Card`, `ToggleButton`, `GridSplitter`, `Popup`. Có `Border`, `GraphicsView`, `TitleBar`, `TabbedPage`, `CollectionView`.
- **[Fact]** `TitleBar` "is only available on Mac Catalyst and Windows": khớp đúng hai nền tảng đích của Trí Thư.
- **[Fact]** Visual State Manager có nhóm `CommonStates` gồm `Normal`, `Disabled`, `Focused`, `Selected`, `PointerOver`; `Normal`, `Disabled`, `Focused`, `PointerOver` được hỗ trợ trên mọi lớp dẫn xuất `VisualElement`.
- **[Fact]** .NET 11 bỏ gói `Microsoft.Maui.Controls.Compatibility` ("no longer built or shipped", từ Preview 6) và mặc định dùng handler `CollectionView2` trên Windows (từ Preview 6).

### 2.3 `UraniumUI.Material` (ứng viên chính)

- **[Fact]** Gói 3.0.0 nhắm `net10.0`, `net10.0-android36.0`, `net10.0-ios26.0`, `net10.0-maccatalyst26.0`, `net10.0-windows10.0.19041`. Nuspec không khai báo license; file `LICENSE` của repo là Apache License 2.0.
- **[Fact]** Chuỗi dependency: `UraniumUI.Material` → `UraniumUI` → `InputKit.Maui` từ 4.6.0, `Microsoft.Maui.Controls` từ 10.0.71, `Plainer.Maui` từ 1.8.0. Không khai báo phụ thuộc gói Compatibility. Nhánh `develop` ngày 26/11/2026 vẫn ghi `net10.0` và `<MauiVersion>10.0.71</MauiVersion>`.
- **[Fact]** Docs "What's New in v3.0": "UraniumUI v3 targets .NET 10 only. .NET 9 support remains available in UraniumUI v2.16.0." Đã có guide migration 3.1 (đang làm), phần đã đọc không nhắc .NET 11.
- **[Fact]** Thành phần đúng tên Material: năm class nút `ElevatedButton`, `FilledButton`, `FilledTonalButton`, `OutlinedButton`, `TextButton` ("implemented by following Material Design Button Guidelines", docs nói hover trên Windows hoạt động); `ButtonView` (card bấm được); `Chip`; class container `PrimaryContainer`, `SecondaryContainer`, `TertiaryContainer`, `ErrorContainer`; class `Elevation0` đến `Elevation5`; `Divider`; `CheckBox`, `RadioButton`; trường nhập `TextField`, `EditorField`, `PickerField`, `DatePickerField`, `TimePickerField`, `AutoCompleteTextField`, `MultiplePickerField`, `DropdownField`, `SelectField`; `TabView`, `BottomSheet`, `DataGrid`, `TreeView`, `Paginator`, `Backdrop`.
- **[Fact]** Thiếu: không có control Card trong thư mục `Controls`; `Chip` chỉ có `Text`, `TextColor`, `IsDestroyVisible`, `SelfDestruct`, `DestroyCommand` (không thấy thuộc tính icon dẫn hoặc content tuỳ ý); không thấy navigation rail, snackbar, dialog host trong danh mục control và docs đã xem.
- **[Fact]** Sáng tối: docs nói rõ "Uranium UI Material Theme on MAUI uses Static Resources... So, they can't be overriden in application. But they can be replaced entirely", thay bằng `ColorsOverride`. Các trang component đã đọc (Buttons, Chip, Containers, Elevation, TextField) đều có bảng ảnh Light và Dark.
- **[Suy luận]** Vì màu là `StaticResource`, cách đổi theme lúc runtime của Trí Thư (thay dictionary, consumer `DynamicResource` tự cập nhật) sẽ không tự áp vào control của UraniumUI. Cần thử trong prototype: thay `ColorsOverride` lúc runtime, hoặc khai báo từng màu trong dictionary thay thế bằng `AppThemeBinding`.
- **[Fact]** Dialog: `IDialogService`, hai gói `UraniumUI.Dialogs.CommunityToolkit` và `UraniumUI.Dialogs.Mopups`; nếu không cài gói popup nào thì UraniumUI dùng page modal.
- **[Fact]** Icon: `UraniumUI.Icons.MaterialIcons` đã deprecated từ 2.8.0, khuyến nghị `UraniumUI.Icons.MaterialSymbols`; cả hai là bộ Material Icons và Material Symbols (docs dẫn tới Google Fonts để so sánh), không phải MDI.
- **[Suy luận]** Tương thích .NET 11 chưa kiểm chứng. App `net11.0-maccatalyst` nhiều khả năng lấy được asset `net10.0-maccatalyst26.0` (cùng platform, phiên bản thấp hơn), nhưng assembly dựng trên MAUI 10 chạy trên MAUI 11 thì phải chạy thử. Gói NuGet 3.0.0 không khai báo phụ thuộc gói Compatibility và hai csproj `UraniumUI`, `UraniumUI.Material` không tham chiếu gói này (repo có PR nâng gói Compatibility, nhiều khả năng ở project mẫu hoặc test, chưa xác minh).

### 2.4 Các ứng viên khác

- **`HorusStudio.Maui.MaterialDesignControls`**: **[Fact]** README: "a collection of UI controls that follow Material Design 3 Guidelines"; có `MaterialCard`, `MaterialChip`, `MaterialSnackbar`, `MaterialDialog`, `MaterialLightTheme`, `MaterialDarkTheme`. Gói 10.0.1 chỉ nhắm Android, iOS, Mac Catalyst, phụ thuộc `Microsoft.Maui.Controls.Compatibility` >= 10.0.10. **[Suy luận]** Với MAUI 11 (bỏ gói Compatibility) và mục tiêu Windows, đây là loại cứng.
- **`Material.Components.Maui`**: **[Fact]** Gói `net8.0`, đăng lần cuối 11/02/2024. README repo tự đánh dấu `TextField`, `Popup`, `ComboBox`, `NavigationDrawer`, `SplitView` là "needs help" trên iOS và Mac.
- **`Shiny.Maui.Controls.Themes.Material`**: **[Fact]** `net11.0`, phụ thuộc `Microsoft.Maui.Controls` >= 11.0.10; `Shiny.Maui.Controls` 3.0.0 có TFM Android, iOS, Windows, không có Mac Catalyst. Là theme màu cho bộ control Shiny (Dialogs, Toast, Fab, ButtonGroup, DataGrid, TreeView...). **[Suy luận]** Quá mới và thiếu TFM Mac Catalyst để đặt cược.
- **`Material.MAUI`**: **[Fact]** "Over 25+ styled Material controls", "Dynamic Theming Engine", "Async Dialog & Snackbar System", phụ thuộc `SkiaSharp.Extended.UI.Maui`. **[Suy luận]** Giai đoạn 0.0.x, một sao, không nên dùng cho sản phẩm.
- **`FlagstoneUI.Themes.Material`**: **[Fact]** Control lõi `FsButton`, `FsEntry`, `FsCard`, `FsEditor`, `FsBorder`; "Themes are standard resource dictionaries". **[Suy luận]** Hữu ích như tham khảo cách viết theme, không đủ làm bộ M3.
- **`Syncfusion.Maui.Toolkit`**: **[Fact]** MIT, cập nhật liên tục. **[Suy luận]** Là bộ control riêng (README nêu ví dụ `SfCartesianChart`), không phải hiện thực Material Design 3; chỉ cân nhắc nếu cần lẻ một control.
- **`Mopups`**: **[Fact]** README: nền tảng hỗ trợ gồm Windows và "MacOS (Mac Catalyst) (This is a bit iffy..)". Gói `net8.0`, bản cuối 31/05/2025. **[Suy luận]** Ưu tiên CommunityToolkit vì đã ra `net11.0`.

### 2.5 Thành phần Trí Thư đang dùng, thay bằng gì

| Thành phần Trí Thư | MAUI dựng sẵn | `UraniumUI.Material` | CommunityToolkit / Mopups | Các ứng viên còn lại |
|---|---|---|---|---|
| `PackIcon` (68 thẻ, 40 Kind) | `Image` + `FontImageSource` với font MDI tự thêm, control `MdiIcon` tự viết | Không khớp: icon là Google Material Symbols | Không có | `IconFont.Maui.MaterialDesignWebIcons` 1.0.0 (font MDI, chưa kiểm chứng tên hằng); `SkiaSharp.Extended.Iconify.MaterialDesignIcons` 2.0.0 |
| `HintAssist.Hint`, `HelperText` | `Placeholder`, `PlaceholderColor`; `Label` phụ | `TextField`, `EditorField` (nhãn nổi) | Không có | Không có ứng viên còn lại dùng được |
| `Card`, `ElevationAssist`, `DropShadowEffect` | `Border` + `StrokeShape` + `Shadow` | Class `Elevation1` đến `Elevation5`, `ButtonView` | Không có | `MaterialCard` (HorusStudio, loại); `FsCard` (Flagstone) |
| `DialogHost` | Page modal qua `PushModalAsync`, hoặc overlay tự viết | `IDialogService` (cần thêm gói popup) | `Popup<T>` + `ShowPopupAsync<T>`; PopupPage | `Mopups` |
| `Popup` neo theo view (2 chỗ) | Không có; overlay tự viết | Không thấy | Chưa kiểm chứng có neo theo view | Không có |
| `Chip` | `Border` + layout (như hiện tại) | `Chip` đơn giản | Không có | `MaterialChip` (HorusStudio, loại) |
| `MessageBox` | `DisplayAlertAsync` | Dialog xác nhận qua `IDialogService` | Popup tự viết; `Snackbar` cho thông báo không chặn | Không có |
| Snackbar tự viết | Overlay `Border` (port nguyên) | Không có | `Snackbar` (Windows cần bật thêm) | `MaterialSnackbar` (loại) |
| Style nút MDT | `Button` + `CornerRadius` + VSM `PointerOver` | Năm class nút Material | Không có | |
| `ColorZoneAssist`, `TabControl` | `TabbedPage` (cấp page); panel nhúng phải tự viết | `TabView` | Chưa kiểm chứng | |
| `CustomColorTheme`, `PaletteHelper`, token | Hai `ResourceDictionary` + `DynamicResource`; hoặc `AppThemeBinding` | `ColorResource`, `StyleResource`, `ColorsOverride` | Không có | Theme là ResourceDictionary (Flagstone, Shiny) |
| Title bar `WindowChrome` | `TitleBar` (chỉ Mac Catalyst và Windows) | Không có | Không có | |

Nguồn: https://api.nuget.org/v3/flatcontainer/uraniumui.material/index.json , https://github.com/enisn/UraniumUI/blob/develop/src/UraniumUI.Material/UraniumUI.Material.csproj , https://github.com/enisn/UraniumUI/blob/develop/docs/en/whats-new/What-Is-New-In-3.0.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/themes/material/ColorsAndStyles.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/themes/material/Buttons.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/themes/material/Chip.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/themes/material/Elevation.md , https://github.com/enisn/UraniumUI/tree/develop/src/UraniumUI.Material/Controls , https://github.com/enisn/UraniumUI/blob/develop/docs/en/dialogs/Index.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/theming/Icons.md , https://api.nuget.org/v3-flatcontainer/horusstudio.maui.materialdesigncontrols/index.json , https://github.com/HorusSoftwareUY/Maui.MaterialDesignControls , https://api.nuget.org/v3-flatcontainer/material.components.maui/index.json , https://github.com/yiszza/mdc-maui , https://api.nuget.org/v3-flatcontainer/shiny.maui.controls/index.json , https://github.com/shinyorg/controls , https://api.nuget.org/v3-flatcontainer/material.maui/index.json , https://github.com/kaysho/MAUI-Material , https://api.nuget.org/v3-flatcontainer/flagstoneui.themes.material/index.json , https://github.com/matt-goldman/flagstone-ui , https://api.nuget.org/v3-flatcontainer/flavor.maui/index.json , https://api.nuget.org/v3-flatcontainer/syncfusion.maui.toolkit/index.json , https://github.com/syncfusion/maui-toolkit , https://api.nuget.org/v3-flatcontainer/communitytoolkit.maui/index.json , https://github.com/CommunityToolkit/Maui , https://api.nuget.org/v3-flatcontainer/mopups/index.json , https://github.com/LuckyDucko/Mopups , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/material-design , https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11 , https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/theming , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/system-theme-changes , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/ , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/titlebar , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/visual-states

## 3. Từng thành phần: thay bằng gì

### 3.1 Dialog kiểu `DialogHost` (modal, trả kết quả về ViewModel)

Hiện trạng: hai `DialogHost` lồng nhau, `IsOpen` bind `Compositions.IsDialogOpen` (hai chiều, bấm ngoài thì đóng) và `DocumentsSurface.IsModalOpen`; `CloseOnClickAway="True"`; `WorkspaceDocumentsView` dùng chung ở hai host (modal và inline).

| Cách | Cơ chế | Ưu | Nhược |
|---|---|---|---|
| CommunityToolkit `Popup<T>` | `Popup<T>` là view; `ShowPopupAsync<T>` trả `IPopupResult<T>` có `Result` và `WasDismissedByTappingOutsideOfPopup`; `PopupOptions.CanBeDismissedByTappingOutsideOfPopup` mặc định `true` | Đã ra `net11.0`; đúng ngữ nghĩa "bấm ngoài thì đóng"; có kết quả | API nằm ở Page (`this.ShowPopupAsync`), ViewModel cần một dịch vụ trung gian; không giữ được binding `IsOpen` hai chiều; chưa kiểm chứng có neo theo view |
| Page modal | `Navigation.PushModalAsync(page)` đẩy page lên ngăn xếp modal; trả kết quả bằng `TaskCompletionSource` hoặc truyền dữ liệu qua constructor | Dùng MAUI thuần, không thêm gói; chặn hoàn toàn phần dưới | Là cả page, không phải hộp thoại trong cửa sổ; "bấm ngoài để đóng" phải tự làm; hai hộp thoại lồng nhau phải xếp lại thành luồng tuần tự |
| `Mopups` | Page popup riêng | Tùy biến được | README tự ghi Mac Catalyst "a bit iffy"; gói `net8.0`, chậm cập nhật |
| Window riêng | `Window` thứ hai qua API đa cửa sổ của MAUI | Phù hợp hộp thoại phức tạp | Chưa kiểm chứng MAUI có window modal trên Windows; lệch thiết kế "hộp thoại trong cửa sổ" |
| Overlay tự viết | Lớp `Border` mờ che + `Border` nội dung đặt cuối `Grid` gốc, `IsVisible` bind `IsDialogOpen`; bấm nền mờ set `IsDialogOpen = false` | Giữ nguyên XAML, binding và hai hộp thoại lồng nhau; chạy mọi nền tảng | Tự làm: bẫy focus, phím Esc, Tab; không chặn nhấp chuột ra ngoài nội dung nếu quên nền mờ |

**[Suy luận]** Với Trí Thư, overlay tự viết là cách ít đảo văn bản nhất vì `WorkspaceDocumentsView` đã dùng chung cho hai host và `IsDialogOpen` đã là hai chiều. Nếu muốn dialog theo kiểu gọi và await kết quả, dùng `Popup<T>` của CommunityToolkit qua một `IDialogService`.

### 3.2 `PackIcon`: bộ Material Design Icons trên MAUI và cách giữ tên Kind

- **[Fact]** `PackIconKind` của MDT là enum sinh tự động; 11 tên đặc trưng của Trí Thư (`BookOpenVariant`, `ViewSequential`, `BookOpenPageVariant`, `BookOpenBlankVariant`, `FolderMultipleOutline`, `DatabaseRefresh`, `NotePlusOutline`, `TextBoxOutline`, `WindowClose`, `FormatTitle`, `WeatherSunny`) đều có trong file nguồn. `PackIconDataFactory.cs` ánh xạ `PackIconKind` sang đường vector.
- **[Fact]** Trí Thư dùng 40 Kind: `AlertCircleOutline`, `BookOpenBlankVariant`, `BookOpenPageVariant`, `BookOpenVariant`, `Check`, `CheckCircleOutline`, `ChevronLeft`, `ChevronRight`, `Close`, `ContentCopy`, `DatabaseRefresh`, `Delete`, `DeleteOutline`, `FileDocument`, `FileDocumentOutline`, `FileOutline`, `FilePdfBox`, `FolderMultipleOutline`, `FolderOutline`, `FolderPlusOutline`, `FormatTitle`, `Magnify`, `Minus`, `NotePlusOutline`, `Pencil`, `PencilOutline`, `Plus`, `Refresh`, `Send`, `Settings`, `TextBoxOutline`, `Upload`, `ViewGridOutline`, `ViewSequential`, `WeatherNight`, `WeatherSunny`, `WindowClose`, `WindowMaximize`, `WindowMinimize`, `WindowRestore`.
- **[Fact]** Các gói icon MAUI có nhiều lượt tải là font Google: `IconFont.Maui.MaterialDesignIcons` 1.0.1 (Outlined, Round, Sharp, Two-Tone của `google/material-design-icons`), `AathifMahir.Maui.MauiIcons.Material` 6.0.0, `UraniumUI.Icons.MaterialIcons` và `MaterialSymbols` 3.0.0. Chúng không có tên như `BookOpenVariant`.
- **[Fact]** Bộ MDI thật: repo `Templarian/MaterialDesign`, license "Icons: Apache 2.0", "Fonts: Apache 2.0"; gói npm `@mdi/font` bản 7.4.47. Trên NuGet có `IconFont.Maui.MaterialDesignWebIcons` 1.0.0 ("Material Design Web Icons (pictogrammers.com/library/mdi) as a drop-in icon font", khoảng 1.900 lượt tải) và `SkiaSharp.Extended.Iconify.MaterialDesignIcons` 2.0.0. Chưa kiểm chứng tên hằng và TFM của hai gói này.
- **[Fact]** Đối chiếu 40 Kind của Trí Thư với `meta.json` của MDI (7.447 tên): 39 khớp tên chính (ví dụ `book-open-variant` mã `F14F7`, `view-sequential` mã `F0729`); `settings` không phải tên chính mà là alias của `cog`.
- **[Fact]** MAUI hiển thị icon font bằng `FontImageSource`; từ .NET 10 markup extension `{FontImage}` bị deprecated.

**[Suy luận]** Ba cách, theo thứ tự khuyến nghị:

1. **Tự viết control `MdiIcon : Image`** với thuộc tính `Kind` (enum giữ nguyên tên) và sinh bảng ánh xạ Kind sang glyph từ `meta.json` của MDI 7.4.47, lấy cả `name` lẫn `aliases` (vì `Settings` là alias). MDT và MDI dùng cùng bộ tên (đã đối chiếu ở trên), nên 40 tên của Trí Thư giữ nguyên; ba converter (`BoolToThemeIconConverter`, `WindowStateToMaximizeIconConverter`, `NotificationIconConverter`) chỉ đổi kiểu enum. Công việc nhỏ, không phụ thuộc bên thứ ba.
2. **Tái dùng file `PackIconKind.cs` và `PackIconDataFactory.cs` của MDT (MIT)**: cắt ra 40 icon, vẽ bằng `GraphicsView` hoặc shape `Path`. Được vector sắc nét, đổi màu bằng `Stroke`/`Fill`, không cần nhúng font.
3. **Dùng `SkiaSharp.Extended.Iconify`** nếu muốn vẽ icon ngay trên canvas Skia (Trí Thư đã dùng SkiaSharp).

### 3.3 `HintAssist` (hint của Entry và Editor)

- **[Fact]** `Entry.Placeholder` và `Editor.Placeholder` kiểu `string`: "defines the text that's displayed when the control is empty"; có `PlaceholderColor`.
- **[Fact]** `HelperText` không có tương đương dựng sẵn.
- **[Suy luận]** 17 chỗ `HintAssist.Hint` chia hai nhóm: ô tìm kiếm, ô lọc (chỉ cần placeholder) và ô dạng "Tên Workspace", "Ask a question", ô API key trong Settings (đang dùng `MaterialDesignOutlinedTextBox`, nhãn nổi). Nhóm thứ hai hoặc chấp nhận placeholder thường (công việc gần như bằng 0), hoặc dùng `TextField`/`EditorField` của UraniumUI, hoặc tự viết (xem mục 4).

### 3.4 `Card` và elevation

- **[Fact]** `Border` là "a container control that draws a border, background, or both, around another control", chứa một con. `VisualElement.Shadow` kiểu `Shadow` với `Radius`, `Opacity`, `Offset`, `Brush`.
- **[Suy luận]** Bảy thẻ `materialDesign:Card` và các card vẽ tay trong `Themes/` đều chuyển thành `Border`: `UniformCornerRadius` thành `StrokeShape="RoundRectangle 12"`, `ElevationAssist.Elevation="Dp1"` thành `Shadow`. Ba token `TriThu.Elevation.E1/E2/E3` chuyển thành ba resource `Shadow`, hover đổi `Shadow` qua VSM (hiện đổi `Effect` qua `Trigger IsMouseOver`).

### 3.5 `MessageBox` (ba chỗ)

- **[Fact]** Pop-up của MAUI render bằng "native controls on each platform". Từ .NET 10 `DisplayAlert` và `DisplayActionSheet` bị deprecated, thay bằng `DisplayAlertAsync` và `DisplayActionSheetAsync`. Phải gọi từ UI thread. Docs mô tả overload hai nút của `DisplayAlert` trả `bool`. **[Suy luận]** `DisplayAlertAsync` giữ nguyên các overload đó.
- **[Fact]** "On desktop platforms, alerts can be dismissed with the escape key." Trên Windows, access key của page phía sau vẫn kích hoạt được khi alert đang mở.
- **[Suy luận]** `DesktopPrompts.ConfirmDeleteComposition` đã nằm sau delegate (test thay bằng fake), đổi sang `DisplayAlertAsync` là nhỏ; docs không cho thấy tham số chọn nút mặc định hay icon như `MessageBox`. Hai chỗ còn lại là lỗi khởi tạo và ngoại lệ chưa xử lý: cần có `Page` hoặc `Window` hợp lệ để gọi, nên giữ đường ghi log file như hiện tại và thử trường hợp lỗi trước khi có cửa sổ.

### 3.6 `Chip`

- **[Fact]** MAUI không có control Chip. `Chip` của UraniumUI chỉ có `Text`, `TextColor`, `IsDestroyVisible`, `SelfDestruct`, `DestroyCommand`.
- **[Suy luận]** Trí Thư chỉ dùng một control `Chip` của MDT (cỡ 26, bo góc 8); các chip khác (tab tài liệu, nhà xuất bản, nhãn tài liệu, chip trích dẫn) đã vẽ tay bằng `Border`. Nên chuyển thẻ `Chip` cuối cùng sang `Border` cho đồng bộ, không cần thư viện.

Nguồn: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/views/popup , https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/views/popup/popup-result , https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/views/popup/popup-options , https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/alerts/snackbar , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/pages/navigationpage , https://github.com/LuckyDucko/Mopups , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/master/src/MaterialDesignThemes.Wpf/PackIconKind.cs , https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/master/src/MaterialDesignThemes.Wpf/PackIconDataFactory.cs , https://github.com/Templarian/MaterialDesign/blob/master/LICENSE , https://github.com/Templarian/MaterialDesign/blob/master/meta.json , https://registry.npmjs.org/@mdi/font/latest , https://github.com/jfversluis/IconFont.Maui.MaterialDesignIcons , https://www.nuget.org/packages/IconFont.Maui.MaterialDesignWebIcons/1.0.0 , https://www.nuget.org/packages/SkiaSharp.Extended.Iconify.MaterialDesignIcons/2.0.0 , https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-10 , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/entry , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/editor , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/border , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/shadow , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/pop-ups

## 4. Phần không có sẵn: tự viết tốn bao nhiêu, rủi ro ở đâu

### 4.1 Ước lượng công việc

Mọi con số dưới đây là **[Suy luận]** thô của người research (người-ngày cho một người quen MAUI), chỉ để so sánh tương đối giữa các mục; ticket prototype cần đo lại hai mục khó nhất (dialog, trường nhập nhãn nổi).

| Phần phải làm | Cách | Ước lượng | Rủi ro chính, nhất là backend Windows |
|---|---|---|---|
| Token sáng tối | Chuyển `TriThuTokens(.Dark).xaml` sang MAUI: `SolidColorBrush` thành `Color`, `DropShadowEffect` thành `Shadow`; giữ `IThemeService`, giữ việc thay dictionary và `ThemeChanged` cho canvas Skia | 1 đến 2 | Thấp. Thứ tự dictionary trong `MergedDictionaries` quyết định key nào thắng |
| 28 style, 31 ControlTemplate | `Style` + `VisualStateManager` với trạng thái `PointerOver`, `Checked` tự đặt; MAUI không có `ToggleButton` | 4 đến 6 | Trung bình. Nút native WinUI có hiệu ứng nền và hover riêng; muốn nền trong suốt và state layer M3 thì phải bọc `Border` hoặc đổi template ở handler. Dùng `TapGestureRecognizer` thay `Button` sẽ mất bàn phím và accessibility |
| `MdiIcon` (40 Kind) | Font MDI + `FontImageSource`, hoặc path data | 1 đến 2 | Thấp. Cần kiểm tra glyph render đúng trên Mac Catalyst |
| Dialog overlay | `Grid` gốc, lớp mờ, bind `IsDialogOpen` | 2 đến 3 | Trung bình. Bẫy focus và Esc tự làm; thứ tự z với `Popup` tìm kiếm và snackbar |
| `Popup` neo theo view (2 chỗ) | Overlay ở gốc `Grid`, tự tính vị trí | 2 đến 4 | Trung bình. Bị cha cắt nếu để trong `Grid` hàng `*`; điều hướng bàn phím (lên, xuống, Enter, Esc) tự làm |
| Tooltip (55 chỗ) | Lớp tooltip tự viết hoặc bỏ tooltips trên desktop | 2 đến 3, hoặc 0 nếu bỏ | Trung bình về UX: tooltip là kỳ vọng trên desktop |
| Nhãn nổi kiểu outlined text field | `TextField`/`EditorField` của UraniumUI, hoặc control tự viết | 1 với UraniumUI; 4 đến 6 nếu tự viết | Cao nếu tự viết: trường nhập native khác nhau giữa WinUI `TextBox` và UIKit |
| Title bar | `TitleBar` của MAUI | 1 đến 2 | Thấp về API; ngoại hình trên Mac Catalyst khác Windows |
| `GridSplitter` kéo đổi rộng panel chat | Thumb tự viết với pan gesture | 1 đến 2 | Thấp |
| `KeyBinding` | `KeyboardAccelerator` | 1 đến 2 | Chưa kiểm chứng hành vi phím trên hai nền tảng. `Entry.Completed` trên Windows kích hoạt khi Enter hoặc Tab, ảnh hưởng ô chat và ô tìm kiếm |
| Danh sách và lưới | `CollectionView` (Windows mặc định sang `CollectionView2` từ .NET 11) | 2 đến 3 | Trung bình: đổi handler giữa bản 10 và 11 |

### 4.2 Rủi ro đặc thù backend Windows

- **[Fact]** MAUI trên Windows dựng trên WinUI 3; .NET 11 bỏ hẳn gói `Microsoft.Maui.Controls.Compatibility`. **[Suy luận]** Mọi thư viện UI dựa vào gói này (HorusStudio) không dùng được.
- **[Fact]** Từ .NET 11 Preview 6 Windows dùng handler `CollectionView2` mặc định; tắt được bằng `UseWindowsCollectionView2Handler=false` nếu cần lui. **[Suy luận]** Danh sách ghi chú, lưới thư viện, tin nhắn chat cần thử lại; `DataGrid` của bên thứ ba cũng chịu ảnh hưởng.
- **[Fact]** `Entry.Completed` "is raised when the user has ended input by pressing the Return key on the keyboard, or by pressing the Tab key on Windows". **[Suy luận]** `KeyBinding Enter` hiện tại của Trí Thư phải đổi sang `Completed` hoặc `ReturnCommand`.
- **[Fact]** Khi alert mở, access key của page phía sau trên Windows vẫn kích hoạt được. **[Suy luận]** Cần rà soát shortcut toàn cục (`Ctrl+O`, `Ctrl+ Plus`) khi dialog đang mở.
- **[Suy luận]** Trạng thái hover chỉ có qua VSM `PointerOver`; không có thuộc tính `IsMouseOver` tương đương. Các `MultiDataTrigger` kết hợp `IsMouseOver` với dữ liệu (nút "Lưu thành ghi chú" chỉ hiện khi hover card AI) phải chuyển thành code-behind nhỏ hoặc behavior.
- **[Suy luận]** `MessageBox` ở đường lỗi khởi tạo: `DisplayAlertAsync` cần `Page`; nên giữ crash log ra file và kiểm tra thủ công.

### 4.3 Rủi ro chung

- **[Fact]** Chưa có thư viện M3 nào trong danh sách ra bản cho `net11.0` kèm cả Windows và Mac Catalyst. **[Suy luận]** Chọn UraniumUI nghĩa là chấp nhận chạy asset `net10.0` trên MAUI 11 cho tới khi repo cập nhật.
- **[Suy luận]** Chọn MAUI thuần (không bộ M3 của bên thứ ba) thì mọi thứ ổn định theo nhịp Microsoft, đổi lại phải tự duy trì khoảng 10 control và style nhỏ. Đây là mức rủi ro thấp hơn cho app desktop hai nền tảng so với phụ thuộc một thư viện chưa khớp phiên bản.

Nguồn: https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11 , https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-10 , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/visual-states , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/entry , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/pop-ups , https://learn.microsoft.com/en-us/dotnet/maui/user-interface/handlers/ , https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms

## 5. Đề xuất cho ticket prototype

**Phương án A (mặc định): token và control tự sở hữu trên MAUI thuần.**

- Chuyển `TriThuTokens.xaml` và `TriThuTokens.Dark.xaml` sang `ResourceDictionary` của MAUI, giữ key, giữ `IThemeService.Apply` thay dictionary; `DynamicResource` giữ nguyên ý nghĩa.
- Tự viết: `MdiIcon` (40 Kind, font MDI), overlay dialog, hai overlay neo vị trí, tooltip, thumb kéo panel.
- Dùng dựng sẵn: `Border`, `Shadow`, `DisplayAlertAsync`, `TitleBar`, VSM `PointerOver`, `KeyboardAccelerator`.
- Thêm gói: `CommunityToolkit.Maui` 17.0.0 (MIT, `net11.0`) nếu cần popup có kết quả hoặc snackbar.
- Ưu điểm: chỉ một phụ thuộc UI và phụ thuộc đó đã ra bản .NET 11. Nhược điểm: nhãn nổi kiểu outlined text field và tab nhúng phải tự viết.

**Phương án B (đo song song): UraniumUI.Material 3.0.0 cho các control khó.**

- Dùng UraniumUI cho `TextField`/`EditorField`, `TabView`, năm class nút, class `Elevation`; phần còn lại như phương án A.
- Prototype phải trả lời bốn câu hỏi, theo thứ tự ưu tiên:
  1. Restore và chạy được trên `net11.0-windows10.0.19041` và `net11.0-maccatalyst` với `Microsoft.Maui.Controls` 11.0.10 không (gói chỉ nhắm `net10.0`).
  2. Đổi theme sáng tối lúc runtime có cập nhật control của UraniumUI không (màu là `StaticResource`).
  3. Hover, focus, phím Tab trên Windows có đúng trạng thái M3 không khi trộn style tự viết với class của UraniumUI.
  4. `IDialogService` của UraniumUI hoạt động với `CommunityToolkit.Maui` 17.0.0 không (tài liệu 3.0 ghi dựng với bản 13.0.0).

**[Suy luận]** Khuyến nghị: mở ticket prototype theo phương án A, thêm một nhánh thí điểm nhỏ của phương án B trong cùng ticket để đo bốn câu hỏi trên. Nếu B qua cả bốn điểm, dùng B cho trường nhập và tab; nếu không, A vẫn đủ để đi tiếp vì phần thiếu là control nhỏ, không phải kiến trúc.

Nguồn: https://api.nuget.org/v3-flatcontainer/communitytoolkit.maui/index.json , https://api.nuget.org/v3/registration5-gz-semver2/microsoft.maui.controls/index.json , https://github.com/enisn/UraniumUI/blob/develop/docs/en/whats-new/What-Is-New-In-3.0.md , https://github.com/enisn/UraniumUI/blob/develop/docs/en/themes/material/ColorsAndStyles.md

## Danh sách điểm chưa kiểm chứng

- TFM của `Syncfusion.Maui.Toolkit` 1.0.12 (catalog không liệt kê nhóm dependency).
- Tên hằng số và TFM của `IconFont.Maui.MaterialDesignWebIcons` 1.0.0 và `SkiaSharp.Extended.Iconify.MaterialDesignIcons` 2.0.0.
- Popup của CommunityToolkit và `Mopups` có neo theo một view hay không.
- MAUI có hỗ trợ window modal trên Windows hay không.
- Hành vi `KeyboardAccelerator` và `Shadow` trên từng nền tảng (docs đã đọc không ghi khác biệt).
- Phản hồi của maintainer trong issue #2299 "WinUI 3" (chỉ đọc được tiêu đề và trạng thái).
