# Số vi phạm hiện tại của code cũ (đo cho ticket #117)

Đo ngày 2026-10-24 trên `main` @ `b5a0911`, SDK 10.0.202, nhánh nháp `measure/117-analyzer-baseline` (không merge). Cấu hình đo nằm ở `Directory.Build.props`, `measure.globalconfig`, `CodeMetricsConfig.txt` cùng nhánh; script đếm là `docs/research/count_sarif.py`.

Phạm vi: solution `PdfReaderApp.slnx` (app `src/PdfReaderApp` + test `tests/PdfReaderApp.Tests`). `Inspector_Core` và các file `Inspector_*.cs` ở gốc repo **không** nằm trong solution nên không được đo.

Cách đếm: build `--no-incremental` với `ErrorLog` SARIF, khử trùng theo (rule, file, dòng, cột) vì project tạm `_wpftmp` của WPF chạy analyzer hai lần; bỏ cảnh báo NuGet (NU*) và file trong `obj/`. "note" là mức suggestion.

## 1. Tổng theo cấu hình

| Cấu hình | Tổng | Trong app | Trong test | Thời gian build (warm, `--no-incremental`) |
|---|---|---|---|---|
| Không analyzer (`RunAnalyzers=false`) | 1 (CS0162 trong test) | 0 | 1 | ~9 s |
| SDK `latest-recommended` + `EnforceCodeStyleInBuild` + CA1502/1505/1506 | 714 (562 warning, 152 note) | 182 | 532 | ~9 đến 11 s |
| SDK + Roslynator + Meziantou + Sonar + StyleCop | 5830 (5141 warning, 689 note) | 3177 | 2653 | ~18 đến 24 s |

## 2. Theo họ rule (cấu hình đầy đủ)

| Họ | Tổng | App | Test |
|---|---|---|---|
| StyleCop (SA) | 4133 | 2622 | 1511 |
| Meziantou (MA) | 667 | 183 | 484 |
| SDK CA | 557 | 103 | 454 |
| Sonar (S) | 216 | 123 | 93 |
| SDK IDE | 101 | 34 | 67 |
| Roslynator (RCS) | 100 | 67 | 33 |
| MVVM Toolkit (MVVMTK0042) | 44 | 44 | 0 |
| SYSLIB / xUnit / CS | 12 | 1 | 11 |

Top rule:
- **StyleCop:** SA1101 (`this.` prefix) 1783, SA1200 (using đặt trong namespace) 493, SA1503 (luôn có ngoặc nhọn) 332, SA1309 (field không bắt đầu bằng `_`) 200, SA1633 (header file) 139. Phần lớn là quy ước định dạng trái với style hiện tại của repo (`_field`, using ngoài namespace).
- **SDK CA:** CA1707 (gạch dưới trong tên, toàn bộ là tên test) 410, CA1725 (tên tham số override khác base, trong app) 75, CA1861 22, CA1816 18, CA1305 9.
- **SDK IDE:** IDE0028 (collection initializer) 101.
- **MVVM Toolkit:** MVVMTK0042 (field-based `[ObservableProperty]` nên chuyển sang partial property) 44, toàn bộ trong ViewModel.
- **Meziantou (app):** MA0006 (`string.Equals` thay `==`) 34, MA0048 (tên file khớp tên type) 27, MA0003 (đặt tên tham số bool) 24, MA0007 (dấu phẩy cuối) 18, MA0016 10, MA0051 10, MA0004 (ConfigureAwait) 7.
- **Sonar (app):** S927 (tên tham số khớp base, trùng CA1725) 27, S1541 11, S108 (block rỗng) 8, **S2077 (SQL ghép chuỗi, security hotspot) 8**, S3776 7, S2486 (catch nuốt lỗi) 7.
- **Roslynator (app):** RCS1123 (thêm ngoặc cho rõ) 22, RCS1037 (khoảng trắng cuối dòng) 21, RCS1001 8.

## 3. Độ phức tạp và độ dài (chỉ app; test không có vi phạm)

Ngưỡng mặc định SDK (CA1502 > 25, CA1505 < 10): **0** vi phạm CA1502/CA1505; CA1506 (coupling) 3.
Ngưỡng chặt hơn: CA1502 > 15, S1541 > 10, S3776 > 15, MA0051 > 60 dòng / 40 statement.

| Method | CA1502 cyclomatic | S1541 | S3776 cognitive | MA0051 dòng |
|---|---|---|---|---|
| `PageLayoutCalculator.Compute` (`Core/PageLayoutCalculator.cs:24`) | 17 | 18 | 29 | 77 |
| `NotesViewModel.Save` (`ViewModels/NotesViewModel.cs:152`) | 17 | 17 | 29 | 42 statement |
| `PdfViewerControl.xaml.cs:683` | | 12 | 24 | 70 |
| `PdfViewerControl.xaml.cs:250` | | 16 | 23 | 70 |
| `AiChatService.cs:51` | | 13 | 22 | 82 |
| `SearchNormalizer.cs:46` | | 12 | 19 | |
| `MainViewModel.cs:1057` | | | 16 | 68 |
| `SqliteDocumentIndex.cs:66` | | | | 79 |
| `MainViewModel.cs:337` (ctor) | | | | 66 |
| `SqliteDocumentIndex.cs:404` | | | | 65 |
| `DocumentIndexingService.cs:24` | | 11 | | 64 |
| `TextSelectionResolver.cs:17`, `SqliteDocumentIndex.cs:257`, `MainViewModel.cs:674`, `PdfViewerControl.xaml.cs:926` | | 11 | | |

CA1506 coupling: `PdfViewerControl` 124 type / 31 namespace, `MainViewModel` 118 type / 23 namespace, ctor `MainViewModel` 44 type.

File dài: `MainViewModel.cs` 1250 dòng, `PdfViewerControl.xaml.cs` 1009 dòng. S104 (độ dài file) bật nhưng không báo, chưa rõ vì sao (có thể cần `SonarLint.xml`).

## 4. File nhiều vi phạm nhất

Chỉ SDK: `MainWindow.xaml.cs` 78, `MainViewModelTests.cs` 66, `NotesViewModelTests.cs` 33, `MainViewModel.cs` 31, `PdfViewerControl.xaml.cs` 18.
Đầy đủ: `MainViewModel.cs` 768, `PdfViewerControl.xaml.cs` 583, `MainViewModelTests.cs` 333, `MainWindow.xaml.cs` 328, `NotesViewModel.cs` 225.

## 5. Chưa đo / chưa chắc

- Thời gian build đo trên máy dev, dao động vài giây giữa các lần; chỉ dùng để so tương đối.
- Chưa đo build incremental (một file đổi) khi có đủ gói, là con số quan trọng cho hook agent.
- Chưa kiểm tra có cảnh báo nào là báo nhầm do source generator của MVVM Toolkit.
