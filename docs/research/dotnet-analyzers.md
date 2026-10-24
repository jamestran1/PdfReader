# Bộ analyzer cho .NET 10 WPF (research cho #116)

Ngày: 2026-10-24. Ticket: #116, map cha: #114 "Review code tự động".

Câu hỏi: những công cụ kiểm tra code bằng máy nào phù hợp cho dự án WPF `net10.0-windows` này, cấu hình ngưỡng độ phức tạp ra sao, `dotnet format` dùng thế nào cho CI và hook agent, và làm sao áp chuẩn mà không làm đỏ build với code cũ.

Tài liệu này chỉ liệt kê lựa chọn và đánh đổi. Quyết định rule cụ thể để dành cho ticket grilling/ADR.

## Tóm tắt

- SDK .NET 10 đã có sẵn bộ CA (code quality) và IDE (code style). Chỉ cần `AnalysisLevel`/`AnalysisMode` + `.editorconfig` + `EnforceCodeStyleInBuild` là có lớp chặn cơ bản, không cần gói ngoài.
- Gói ngoài đều còn bảo trì tháng 10/2026: Roslynator 5.0.1, Meziantou.Analyzer 3.0.316, SonarAnalyzer.CSharp 10.35. StyleCop.Analyzers chỉ có bản beta (1.3.x) cho C# 9 trở lên, bản stable cuối là 2019.
- Độ phức tạp: CA1502/CA1505/CA1506 tắt mặc định, KHÔNG được bật bởi `AnalysisMode=All`, phải bật từng rule và chỉnh ngưỡng qua `CodeMetricsConfig.txt` (AdditionalFiles). Độ dài method: Meziantou MA0051 hoặc Sonar S138; cognitive complexity: Sonar S3776.
- Đo thực tế trên repo (SDK 10.0.202): `latest-recommended` sinh 554 cảnh báo (100 ở app, phần lớn còn lại là CA1707 trong test), `latest-all` sinh 983. Build hiện tại không có cảnh báo CA/CS nào, chỉ có NU (NuGet).
- `dotnet format whitespace --folder --include <file>` mất khoảng 2,8 s một file; chế độ project mất 5,5 s; `style`/`analyzers` khoảng 8,5 s. Đủ nhanh cho hook nhưng không "tức thì".
- Gotcha WPF: project tạm `*_wpftmp.csproj` biên dịch lại cùng mã nguồn nên mỗi cảnh báo CA ở app bị in hai lần. Bất kỳ script đếm/lọc cảnh báo nào cũng phải khử trùng theo (file, dòng, cột, ID).

## 1. Analyzer có sẵn trong SDK

### 1.1 Các thuộc tính MSBuild

| Thuộc tính | Ý nghĩa | Mặc định cho net10.0 |
|---|---|---|
| `EnableNETAnalyzers` | Bật bộ CA của SDK | `true` cho .NET 5+ |
| `AnalysisLevel` | Bộ rule theo phiên bản .NET (`10`, `latest`, `preview`), có thể ghép mode: `latest-recommended` | `latest` |
| `AnalysisMode` | `None` < `Default` < `Minimum` < `Recommended` < `All` | `Default` |
| `AnalysisLevel<Category>` / `AnalysisMode<Category>` | Như trên nhưng cho một category (Security, Performance...) | theo giá trị chung |
| `EnforceCodeStyleInBuild` | Chạy rule IDExxxx khi build dòng lệnh | `false` |
| `CodeAnalysisTreatWarningsAsErrors` | Khi có `-warnaserror`, có biến CA thành lỗi hay không | theo `TreatWarningsAsErrors` |
| `TreatWarningsAsErrors`, `WarningsAsErrors`, `WarningsNotAsErrors` | Nâng cảnh báo thành lỗi | tắt |
| `ErrorLog` | Xuất SARIF mọi diagnostic của compiler + analyzer (`file.sarif,version=2.1`) | tắt |

Nguồn: [MSBuild reference, Code analysis properties](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#code-analysis-properties); [Code analysis overview](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview); [Compiler options: errors and warnings](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-options/errors-warnings).

Chi tiết cần nhớ:

- `AnalysisLevel=latest` nghĩa là mỗi lần nâng SDK sẽ có rule mới được bật. Nếu muốn build không tự đỏ khi CI nâng SDK, khóa bằng số (`10` hoặc `10-recommended`). Nguồn: Code analysis overview, mục "Latest updates".
- Nếu dùng `AnalysisMode` (MSBuild) thì cấu hình bật "hàng loạt" trong `.editorconfig` (vd `dotnet_analyzer_diagnostic.category-X.severity`) bị bỏ qua. Cấu hình từng rule vẫn có hiệu lực. Nguồn: [Configuration options, Analysis mode](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-options#analysis-mode).
- Rule IDE chỉ chạy lúc build khi `EnforceCodeStyleInBuild=true` VÀ rule đó được đặt severity `warning`/`error` trong `.editorconfig`. Một số rule IDE chỉ chạy trong Visual Studio vì lý do hiệu năng. Từ .NET 9 có thể dùng cú pháp `option = value:warning` và build vẫn tôn trọng. Nguồn: Code analysis overview, mục "Code-style analysis".
- IDE0005 (using thừa) chỉ chạy lúc build khi bật `GenerateDocumentationFile=true` (roslyn issue 41640). Bật cái này lại kéo theo CS1591 (thiếu XML doc) cho mọi member public, thường phải `NoWarn` CS1591. Nguồn: [IDE0005](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0005).
- `TreatWarningsAsErrors` trong csproj cũng biến cảnh báo NuGet thành lỗi. Repo hiện có NU1902/NU1903 (BouncyCastle.Cryptography 2.2.1 qua itext) và NU1701/NU1510, nên bật thẳng sẽ đỏ ngay ở bước restore. Cách tránh: `WarningsNotAsErrors` chứa `NU1901;NU1902;NU1903;NU1904` hoặc chỉ dùng `WarningsAsErrors` cho danh sách ID chọn lọc. Nguồn: [NU1901-NU1904](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1901-nu1904), [Auditing packages](https://learn.microsoft.com/nuget/concepts/auditing-packages).
- Khác biệt `TreatWarningsAsErrors` (chỉ compiler, không xuất output) và switch `-warnaserror` (mọi task MSBuild). Nguồn: errors-and-warnings, mục TreatWarningsAsErrors.

### 1.2 `.editorconfig` và `.globalconfig`

- `.editorconfig`: áp theo file/thư mục qua section header (`[*.cs]`, `[tests/**.cs]`). Có thể đặt nhiều file lồng nhau, file sâu hơn thắng. Đây cũng là nơi đặt quy tắc định dạng cho `dotnet format`.
- Global AnalyzerConfig (`.globalconfig`, cần `is_global = true`): áp cho toàn project, không có section, không chỉnh được thiết lập editor. File tên `.globalconfig` tự áp cho mọi project trong thư mục và thư mục con; tên khác phải thêm item `GlobalAnalyzerConfigFiles`, có thể kèm điều kiện MSBuild (vd chỉ cho project test).
- Thứ tự ưu tiên: trong cùng file, dòng sau thắng; giữa hai `.editorconfig`, file sâu hơn thắng; giữa hai globalconfig, `global_level` cao hơn thắng (`.globalconfig` mặc định 100); giữa `.editorconfig` và globalconfig, `.editorconfig` thắng. `-nowarn`/`-warnaserror` trên dòng lệnh luôn thắng mọi file cấu hình.
- Ruleset (`.ruleset`) đã deprecated.

Nguồn: [Configuration files for code analysis rules](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-files).

### 1.3 Mã sinh ra (generated code), WPF và CommunityToolkit.Mvvm

- Analyzer tự bỏ qua file được coi là generated: tên kết thúc `.designer.cs`, `.generated.cs`, `.g.cs`, `.g.i.cs`, tên bắt đầu `TemporaryGeneratedFile_`, hoặc comment đầu chứa `<auto-generated>`. Có thể thêm pattern bằng `generated_code = true` trong `.editorconfig`. Lưu ý: chỉ loại khỏi diagnostic của analyzer, không loại diagnostic của compiler (CS). Nguồn: [Configuration options, Exclude generated code](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-options#exclude-generated-code), [Nullable migration, Generated code is excluded](https://learn.microsoft.com/dotnet/csharp/advanced-topics/update-applications/nullable-migration-strategies#generated-code-is-excluded).
- WPF sinh `*.g.cs`/`*.g.i.cs` trong `obj/` cho mỗi XAML có `x:Class` (MarkupCompilePass1). Vì đuôi `.g.cs` nên analyzer bỏ qua; đo thực tế: 0 cảnh báo CA nằm trong `obj/`. Nguồn: [Compile a WPF Application](https://learn.microsoft.com/dotnet/desktop/wpf/app-development/building-a-wpf-application-wpf#wpf-build-pipeline).
- WPF tạo project tạm `*_wpftmp.csproj` (GenerateTemporaryTargetAssembly) khi XAML tham chiếu type cùng project. Project tạm biên dịch lại toàn bộ mã nguồn, nên analyzer chạy hai lần. Đo thực tế với `latest-recommended`: 200 dòng cảnh báo CA gắn `PdfReaderApp.csproj` và 200 dòng y hệt gắn `PdfReaderApp_xxxx_wpftmp.csproj`. Hệ quả: (a) thời gian analyzer ở project app gần như nhân đôi, (b) script đếm cảnh báo phải khử trùng, (c) tên file `_wpftmp` không cố định giữa các lần build. Nguồn: [GenerateTemporaryTargetAssembly task](https://learn.microsoft.com/visualstudio/msbuild/generatetemporarytargetassembly-task); số đo từ build cục bộ.
- Source generator của CommunityToolkit.Mvvm 8.4.2: mã sinh nằm trong compilation (không phải file trên đĩa), được đánh dấu generated nên analyzer không báo trên đó. Nhưng generator có analyzer riêng (MVVMTKxxxx). Đo thực tế: `dotnet format analyzers --severity info` trên `MainViewModel.cs` báo hàng loạt MVVMTK0042 ("field dùng `[ObservableProperty]` nên đổi sang partial property ... để generator và analyzer khác thấy property được sinh"). Nếu nâng severity info lên chặn thì toàn bộ ViewModel cũ đỏ. Nguồn: [MVVMTK0042](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0042).
- Hệ quả của field-based `[ObservableProperty]`: analyzer bên thứ ba nhìn thấy field `_foo` chứ không thấy property `Foo`. Đây là lý do MVVM Toolkit khuyên partial property. Chưa kiểm chứng rule cụ thể nào báo nhầm vì điều này trên repo.
- Code-behind WPF (`MainWindow.xaml.cs`) là nơi tập trung cảnh báo nhất ở app (78 với `latest-recommended`, 98 với `latest-all`). Có thể cần severity riêng cho `*.xaml.cs` (event handler, `async void`).

## 2. Gói analyzer ngoài

Phiên bản lấy từ NuGet API và GitHub ngày 2026-10-24.

| Gói | Bản mới nhất | Ngày | Giấy phép | Bắt loại vấn đề gì |
|---|---|---|---|---|
| Microsoft.CodeAnalysis.NetAnalyzers | 10.0.402 (stable), 11.0.100-rc.2 | 2026-10-13 | MIT | Chính là bộ CA trong SDK. Chỉ cần gói riêng khi muốn analyzer mới hơn SDK đang dùng. |
| Roslynator.Analyzers (+ Roslynator.Formatting.Analyzers) | 5.0.1 | 2026-10-04 | Apache-2.0 | Khoảng 200 rule RCS về đơn giản hóa, code thừa, đọc dễ; gói Formatting thêm rule định dạng. Có CLI (`roslynator analyze/fix`) chạy độc lập build. |
| StyleCop.Analyzers | 1.1.118 stable (2019); 1.3.151-beta | 2026-10-21 | MIT | Quy ước trình bày SAxxxx: thứ tự member, khoảng trắng, XML doc, đặt tên. README: C# 9 đến 15 cần nhánh 1.3.0-beta. |
| Meziantou.Analyzer | 3.0.316 | 2026-10-21 | MIT | 240 rule MA: bug thực tế (thiếu `StringComparer`, `IFormatProvider`, `CancellationToken`), async (MA0045 blocking call, MA0134 bỏ quên kết quả async), độ dài method MA0051. Phát hành rất dày (903 phiên bản). |
| SonarAnalyzer.CSharp | 10.35.0.4138 | 2026-09-28 | SONAR Source-Available License v1.0 (từ 11/2024) | Bug, code smell, security hotspot; có rule độ phức tạp S3776 (cognitive), S1541 (cyclomatic), S138 (dòng/method), S104 (dòng/file), S107 (số tham số). |

Nguồn: NuGet registration API (`api.nuget.org/v3/registration5-gz-semver2/<id>/index.json`); README và LICENSE các repo [dotnet/roslynator](https://github.com/dotnet/roslynator), [DotNetAnalyzers/StyleCopAnalyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers), [meziantou/Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer), [SonarSource/sonar-dotnet](https://github.com/SonarSource/sonar-dotnet).

### 2.1 Chồng lấn

- Roslynator và IDE rule của SDK trùng nhiều (đơn giản hóa biểu thức, using thừa, braces). Bật cả hai dễ có hai cảnh báo cho cùng một chỗ, thậm chí khuyên ngược nhau.
- StyleCop và IDE0055/`dotnet format whitespace` trùng ở định dạng; StyleCop còn đòi XML doc (SA1600) rất ồn cho app (không phải thư viện).
- Meziantou trùng một phần CA về culture/comparer (MA0011 ~ CA1305, MA0002 ~ CA1309/CA1310, MA0004 ~ CA2007). Meziantou thường bắt rộng hơn.
- Sonar trùng rộng với CA và Roslynator; điểm riêng đáng giá là S3776 cognitive complexity và bộ rule length.

### 2.2 Lưu ý riêng theo gói

- **Meziantou MA0004 (ConfigureAwait):** bật mặc định. Mặc định `MA0004.report = DetectContext` cố đoán ngữ cảnh UI; trong WPF thêm `ConfigureAwait(false)` ở code-behind/ViewModel có thể làm continuation chạy ngoài UI thread. Nếu dùng gói này cần quyết định rõ severity cho MA0004. Nguồn: [MA0004](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0004.md).
- **Meziantou MA0051:** cấu hình trong `.editorconfig`: `MA0051.maximum_lines_per_method = 60`, `MA0051.maximum_statements_per_method = 40` (giá trị ví dụ trong doc). Nguồn: [MA0051](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0051.md).
- **Sonar:** bật/tắt rule bằng `.editorconfig`/`.globalconfig` như mọi analyzer Roslyn, nhưng THAM SỐ (vd ngưỡng S3776, mặc định 15) chỉ đổi được qua file `SonarLint.xml` thêm làm `AdditionalFiles`. Trong profile "Sonar way" có S3776 và S107; S138, S104, S1541 không nằm trong profile mặc định. Giấy phép SSAL không phải OSI; repo này là AGPL-3.0 nhưng analyzer chỉ là dev dependency (`PrivateAssets=all`), không phân phối cùng MSIX. Chưa kiểm tra pháp lý chi tiết. Nguồn: README và `analyzers/rspec/cs/*.json`, `Sonar_way_profile.json` trong sonar-dotnet.
- **StyleCop:** phải dùng bản beta cho C# 14 (.NET 10). Đây là rủi ro bảo trì: không có bản stable mới từ 2019, dù repo vẫn hoạt động.
- **Roslynator:** CLI có thể chạy analyze trên solution không cần build lại, hữu ích cho báo cáo đợt dọn code cũ.

## 3. Đo độ phức tạp

### 3.1 Bộ code metrics của SDK

| Rule | Đo gì | Ngưỡng mặc định |
|---|---|---|
| CA1501 | Độ sâu kế thừa (bỏ qua type trong `System.*`) | 5 |
| CA1502 | Cyclomatic complexity | > 25 cho method |
| CA1505 | Maintainability index | < 10 |
| CA1506 | Class coupling (số type tham chiếu duy nhất) | 95 cho type, 40 cho các symbol khác |
| CA1509 | Báo lỗi cú pháp trong `CodeMetricsConfig.txt` | phải bật trong `.globalconfig`, không bật được ở `.editorconfig` |

- Tất cả tắt mặc định ("Enabled by default in .NET 10: No") và KHÔNG được bật bởi `AnalysisMode=All` hay `latest-all`. Phải bật từng rule: `dotnet_diagnostic.CA1502.severity = warning`.
- Ngưỡng cấu hình qua file text `CodeMetricsConfig.txt` khai báo là `AdditionalFiles`. Cú pháp `RuleId(SymbolKind): Threshold`, ví dụ `CA1502: 10`, `CA1502(Type): 4`, `CA1506(Method): 60`. SymbolKind hợp lệ: Assembly, Namespace, Type, Method, Field, Event, Property. Nếu khai trong `Directory.Build.props` phải dùng `$(MSBuildThisFileDirectory)CodeMetricsConfig.txt`.
- Cyclomatic theo CA1502 = 1 + số nhánh (`if`, `while`, `do`...) + số `case`.
- Không có rule SDK nào đo trực tiếp số dòng method/class. Độ dài cần Meziantou MA0051 hoặc Sonar S138/S104.

Nguồn: [CA1501](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1501), [CA1502](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502), [CA1505](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1505), [CA1506](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1506), [CA1509](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1509), [Generate code metrics data](https://learn.microsoft.com/visualstudio/code-quality/how-to-generate-code-metrics-data), [msbuild-props, AnalysisMode](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#analysismode).

### 3.2 Lựa chọn

| Lựa chọn | Ưu | Nhược |
|---|---|---|
| Chỉ CA1502/CA1505/CA1506 | Không thêm gói; MIT; nằm sẵn trong build | Cyclomatic không phản ánh tốt độ khó đọc; không đo dòng; config qua AdditionalFiles |
| + Meziantou MA0051 | Đo dòng/statement mỗi method, config trong `.editorconfig` | Kéo theo 240 rule khác (tắt bớt bằng `.globalconfig`) |
| + Sonar S3776/S138/S104 | Cognitive complexity, độ dài method và file | Giấy phép SSAL; tham số qua `SonarLint.xml`; rất nhiều rule chồng lấn |

Số đo độ phức tạp trên repo hiện tại chưa chạy (cần thêm file cấu hình vào project, ngoài phạm vi research). Ticket theo dõi chất lượng theo thời gian có thể làm việc này.

## 4. `dotnet format`

### 4.1 Chế độ

| Lệnh | Làm gì | Cần load project? |
|---|---|---|
| `dotnet format whitespace` | Chỉ định dạng khoảng trắng theo `.editorconfig` | Không nếu dùng `--folder` |
| `dotnet format style` | Sửa rule IDE theo `.editorconfig` | Có (cần semantic model) |
| `dotnet format analyzers` | Sửa rule analyzer khác (CA, gói ngoài) có code fix | Có |
| `dotnet format` | Cả ba | Có |

Tùy chọn chính: `--verify-no-changes` (exit khác 0 nếu có thay đổi, dùng cho CI), `--include`/`--exclude` (đường dẫn tương đối, cách nhau bằng dấu cách), `--diagnostics`/`--exclude-diagnostics` (lọc ID), `--severity info|warn|error` (mặc định `warn`), `--no-restore`, `--report <dir>` (JSON), `--include-generated` (mặc định KHÔNG định dạng file do SDK sinh), `--folder` (chỉ cho `whitespace`). Nguồn: [dotnet format](https://learn.microsoft.com/dotnet/core/tools/dotnet-format).

### 4.2 Số đo trên repo (một file, máy dev, SDK 10.0.202, đã restore)

| Lệnh trên `src/PdfReaderApp/ViewModels/MainViewModel.cs` với `--verify-no-changes` | Thời gian | Kết quả |
|---|---|---|
| `whitespace --folder --include <file>` | 2,8 s | exit 2, 2 lỗi WHITESPACE (dòng 699) |
| `whitespace <csproj> --include <file>` | 5,5 s | exit 2, cùng kết quả |
| `style <csproj> --no-restore --include <file> --severity info` | 8,6 s | exit 2, IDE0028/IDE0059/IDE0090/IDE0301... |
| `analyzers <csproj> --no-restore --include <file> --severity info` | 8,4 s | exit 2, CA1816 + MVVMTK0042 |
| So sánh: `dotnet build` app không đổi gì (incremental) | 4,9 s | |
| So sánh: build full solution từ đầu | khoảng 15 s | |

Nhận xét:

- Chi phí chủ yếu là khởi động và load MSBuild workspace, không phải kích thước file. `--include` một file không làm `style`/`analyzers` nhanh hơn nhiều vì vẫn phải load và biên dịch cả project.
- Cho hook agent (chạy mỗi lần sửa file), `whitespace --folder --include <file>` là lựa chọn rẻ nhất. `style`/`analyzers` mỗi lần sửa tốn khoảng 8 đến 9 s; có thể để ở bước cuối (Stop hook) hoặc chỉ ở CI.
- Chưa có `.editorconfig` nên số đo dùng mặc định; code hiện tại đã có vi phạm whitespace ngay cả với mặc định.
- `--verify-no-changes` coi cả diagnostic mức `info` là thay đổi nếu đặt `--severity info`. Ở CI nên để mặc định `warn` để khớp với những gì `.editorconfig` đánh dấu.

## 5. Áp chuẩn mà không làm đỏ build với code cũ

### 5.1 Hiện trạng đo được

Build mặc định hiện tại: 0 cảnh báo CA/CS/IDE, chỉ có NU1902 (18 dòng), NU1701 (18), NU1903 (6), NU1510 (4).

Build thử bằng tham số dòng lệnh (không sửa file project): `-p:AnalysisLevel=... -p:EnforceCodeStyleInBuild=true --no-incremental`, đã khử trùng theo dòng:

| Mức | Tổng | ID khác nhau | Ở app (`src/`) | Rule nhiều nhất |
|---|---|---|---|---|
| `latest-recommended` | 554 | 15 | 100 | CA1707 410 (100% ở test, tên kiểu `Method_Should_X`), CA1725 76, CA1861 22, CA1816 18, CA1305 9 |
| `latest-all` | 983 | 31 | 316 | CA1707 410, CA1515 111, CA2000 97, CA1725 76, CA1307 71, CA1031 39, CA1063 36, CA1062 27 |

Không có rule IDE nào xuất hiện vì chưa có `.editorconfig` đặt severity. Các file nhiều nhất: `MainWindow.xaml.cs`, `MainViewModelTests.cs`, `NotesViewModelTests.cs`, `MainViewModel.cs`.

### 5.2 Kỹ thuật

1. **Baseline bằng severity (cách chuẩn của Roslyn):** bật rule ở `suggestion`/`silent` cho toàn repo, nâng lên `warning`/`error` từng rule khi đã dọn xong. Không có file baseline kiểu "danh sách vi phạm cũ" trong SDK. Visual Studio có "Build and Suppress Active Issues" sinh `GlobalSuppressions.cs` với `SuppressMessage` cho từng vi phạm; cách này chỉ có trong VS và tạo file suppress rất dài. Nguồn: [Suppress code analysis violations](https://learn.microsoft.com/visualstudio/code-quality/in-source-suppression-overview), [How to suppress code analysis warnings](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/suppress-warnings).
2. **Severity theo thư mục:** `.editorconfig` cho phép section theo đường dẫn, ví dụ `[tests/**.cs]` hạ CA1707 xuống `none`, hoặc `[src/PdfReaderApp/Legacy/**.cs]` để mức nhẹ hơn. Cũng có thể đặt `.editorconfig` con trong thư mục. `.globalconfig` thì áp cả project, phù hợp cho cấu hình theo project (vd file riêng cho test qua `GlobalAnalyzerConfigFiles` có điều kiện).
3. **Severity theo file "đã sạch" (allowlist):** liệt kê file đã dọn vào section `[{src/.../A.cs,src/.../B.cs}]` với severity `error`, còn lại `warning`/`suggestion`. Tăng dần danh sách theo đợt dọn. Có thể ngược lại (denylist file cũ). Theo dõi tiến độ bằng số file trong danh sách.
4. **Chỉ chặn trên file trong diff:** SDK không có tính năng này. Các cách tự dựng:
   - `dotnet format ... --verify-no-changes --include <danh sách file đổi từ git diff>`: đơn giản, nhưng chỉ phát hiện rule có code fix (whitespace, style, analyzers có fixer). Rule không có fixer (vd CA1502) không bị báo bởi `dotnet format`.
   - Build với `ErrorLog=<file>.sarif,version=2.1`, rồi script lọc kết quả theo file (hoặc theo dòng) trong diff và fail nếu còn lỗi. Bắt được mọi analyzer, kể cả code metrics. Phải khử trùng do `_wpftmp`.
   - Dùng tính năng code scanning của GitHub để upload SARIF và chỉ hiện cảnh báo mới trên PR. Chưa nghiên cứu ở đây (thuộc ticket CI).
5. **Khóa `AnalysisLevel` theo số** để nâng SDK không tự thêm rule.
6. **`WarningsAsErrors` danh sách ID thay vì `TreatWarningsAsErrors`** để không kéo theo NU và rule chưa dọn.

### 5.3 Đánh đổi chính

| Cách | Ưu | Nhược |
|---|---|---|
| Severity thấp toàn cục, nâng dần theo rule | Đơn giản, chỉ dùng tính năng SDK | Code mới cũng không bị chặn với rule chưa nâng |
| Allowlist file sạch ở `error` | Code đã dọn không thoái lui; đo tiến độ dễ | File mới phải tự thêm vào danh sách (hoặc dùng pattern thư mục) |
| Lọc SARIF theo diff | Chặn mọi rule trên code đang sửa, code cũ để yên | Phải tự viết và bảo trì script; sửa một dòng trong file cũ có thể bị bắt dọn cả file nếu lọc theo file |
| `dotnet format --include` diff | Nhanh, có sẵn | Chỉ bao rule có code fix |

## 6. Ghi chú cho hook Claude Code

- Hook chạy khi agent sửa file nên dùng lệnh rẻ: `dotnet format whitespace --folder --include <file> --verify-no-changes` (khoảng 3 s). Có thể chạy không `--verify-no-changes` để tự sửa.
- Kiểm tra analyzer thực sự (CA, gói ngoài, code metrics) cần build; build incremental project app khoảng 5 s khi không đổi gì, lâu hơn khi có thay đổi vì WPF chạy hai lần compile (`_wpftmp`). Hợp lý hơn khi gom lại một lần ở cuối lượt sửa.
- Hook chạy trong worktree khác nhau có `obj/` riêng, lần đầu chậm như build sạch.

## Chưa kiểm chứng

- Thời gian analyzer tăng thêm khi bật từng gói ngoài (chưa thêm gói vào project, theo yêu cầu research).
- Số vi phạm CA1502/CA1505/CA1506 và MA0051/S3776 trên repo hiện tại.
- Rule nào của gói ngoài báo nhầm vì `[ObservableProperty]` dạng field.
- Ảnh hưởng pháp lý của giấy phép SSAL (Sonar) với repo AGPL.
- Tên "Roslynator khoảng 200 rule" là ước lượng, chưa đếm.
