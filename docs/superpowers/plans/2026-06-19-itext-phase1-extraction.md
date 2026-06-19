# iText Phase 1: Extraction + Service Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add iText 8 as PDF manipulation library, establish `IPdfDocumentService` facade with `ITextPdfDocumentService` implementation, implement `PdfCoordinateMapper`, and wire iText text extraction into `PdfStructureAnalyzer` to replace the current `Split('\n')` heuristic.

**Architecture:** iText 8 is added alongside PDFium (hybrid — no replacement). `IPdfDocumentService` is a new facade exposing Phase 1 extraction. `MainViewModel` receives it via constructor injection (default `ITextPdfDocumentService`). `PdfStructureAnalyzer` is refactored to delegate to the service. `PdfCoordinateMapper` is a pure-function utility for coordinate conversion; it is not consumed in Phase 1 but is required groundwork for Phase 2 annotations.

**Tech Stack:** iText Core 8 (NuGet: `itext` v8.0.5), xUnit 2.9.3, .NET 10, CommunityToolkit.Mvvm 8.4.2

## Global Constraints

- Target framework: `net10.0-windows`; test project also `net10.0-windows`
- Nullable: enabled — all reference types must be non-null or explicitly nullable
- No `Co-Authored-By` trailer in any commit message
- Do NOT add or commit `conductor/` or `.serena/` directories
- iText is AGPL v3 — already accepted per spec; do not add commercial workarounds
- `PdfStructureAnalyzer.TextChunk` class must stay compilable during transition (backward-compat shim)
- No `--no-verify` on commits

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/PdfReaderApp/PdfReaderApp.csproj` | Modify | Add `itext` NuGet package reference |
| `src/PdfReaderApp/Models/TextBlock.cs` | Create | Rich text extraction result DTO |
| `src/PdfReaderApp/Core/PdfCoordinateMapper.cs` | Create | Pure-function PDF user-space (bottom-left, points) to render-space (top-left, pixels) conversion |
| `src/PdfReaderApp/Services/IPdfDocumentService.cs` | Create | Manipulation facade interface (Phase 1: LoadFile + ExtractStructure) |
| `src/PdfReaderApp/Services/ITextPdfDocumentService.cs` | Create | iText 8 implementation; opens doc lazily on LoadFile, disposes on Dispose() |
| `src/PdfReaderApp/Services/PdfStructureAnalyzer.cs` | Modify | Replace PDFium `Split('\n')` with IPdfDocumentService; keep TextChunk shim |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | Modify | Inject IPdfDocumentService; call LoadFile on OpenFile; build context from analyzer |
| `tests/PdfReaderApp.Tests/Core/PdfCoordinateMapperTests.cs` | Create | Unit tests for coordinate math |
| `tests/PdfReaderApp.Tests/Services/ITextPdfDocumentServiceTests.cs` | Create | Integration tests against programmatically created PDF |

---

### Task 1: Add iText dependency and TextBlock model

**Files:**
- Modify: `src/PdfReaderApp/PdfReaderApp.csproj`
- Create: `src/PdfReaderApp/Models/TextBlock.cs`

**Interfaces:**
- Produces: `TextBlock` record used by Tasks 3 and 4

- [ ] **Step 1: Add iText package reference**

Edit `src/PdfReaderApp/PdfReaderApp.csproj` — add inside the existing `<ItemGroup>` with the other PackageReferences:

```xml
<PackageReference Include="itext" Version="8.0.5" />
```

- [ ] **Step 2: Create TextBlock model**

Create `src/PdfReaderApp/Models/TextBlock.cs`:

```csharp
namespace PdfReaderApp.Models;

public sealed record TextBlock(
    string Text,
    float PdfX,
    float PdfY,
    float Width,
    float Height,
    float FontSize,
    int PageIndex,
    string StructureType);
```

- [ ] **Step 3: Restore and build**

```bash
dotnet restore src/PdfReaderApp/PdfReaderApp.csproj
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
```

Expected: Build succeeds. `itext` appears in restore output.

- [ ] **Step 4: Commit**

```bash
git add src/PdfReaderApp/PdfReaderApp.csproj src/PdfReaderApp/Models/TextBlock.cs
git commit -m "feat: add iText 8 dependency and TextBlock model (Phase 1)"
```

---

### Task 2: PdfCoordinateMapper

**Files:**
- Create: `src/PdfReaderApp/Core/PdfCoordinateMapper.cs`
- Create: `tests/PdfReaderApp.Tests/Core/PdfCoordinateMapperTests.cs`

**Interfaces:**
- Produces:
  - `PdfCoordinateMapper(float pageHeightPt, float scale, int dpi)`
  - `(float x, float y) PdfPointToRender(float pdfX, float pdfY)`
  - `(float x, float y) RenderPointToPdf(float renderX, float renderY)`

**Background:** PDF user-space has origin at bottom-left (points, 1 pt = 1/72 inch). Render-space has origin at top-left (pixels). At `dpi=72, scale=1.0`, 1 point = 1 pixel. Formula: `renderX = pdfX * scale * (dpi/72)`, `renderY = (pageHeightPt - pdfY) * scale * (dpi/72)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Core/PdfCoordinateMapperTests.cs`:

```csharp
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class PdfCoordinateMapperTests
{
    // US Letter: 612 x 792 points. At dpi=72, scale=1.0: 1 point = 1 pixel exactly.
    private static PdfCoordinateMapper LetterAt72Dpi() => new(792f, 1.0f, 72);

    [Fact]
    public void PdfPointToRender_BottomLeft_MapsToBottomOfRenderSpace()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(0f, 0f);
        Assert.Equal(0f, rx);
        Assert.Equal(792f, ry); // PDF y=0 (bottom) -> render y=pageHeight
    }

    [Fact]
    public void PdfPointToRender_TopLeft_MapsToTopOfRenderSpace()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(0f, 792f);
        Assert.Equal(0f, rx);
        Assert.Equal(0f, ry); // PDF y=792 (top) -> render y=0
    }

    [Fact]
    public void PdfPointToRender_KnownPoint_CorrectPixelPosition()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(100f, rx);
        Assert.Equal(492f, ry); // (792 - 300) * 1.0 * 1.0 = 492
    }

    [Fact]
    public void PdfPointToRender_HighDpi_ScalesPixels()
    {
        var mapper = new PdfCoordinateMapper(792f, 1.0f, 144); // 144 dpi = 2x
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(200f, rx); // 100 * 2
        Assert.Equal(984f, ry); // (792 - 300) * 2 = 984
    }

    [Fact]
    public void PdfPointToRender_WithScale_ScalesOutput()
    {
        var mapper = new PdfCoordinateMapper(792f, 2.0f, 72); // 2x zoom
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(200f, rx); // 100 * 2.0
        Assert.Equal(984f, ry); // (792 - 300) * 2.0
    }

    [Fact]
    public void RenderPointToPdf_RoundTrip_ReturnsOriginalPoint()
    {
        var mapper = LetterAt72Dpi();
        var (px, py) = mapper.RenderPointToPdf(100f, 492f);
        Assert.Equal(100f, px);
        Assert.Equal(300f, py);
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL (class not yet created)**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep -E "error|warning" | head -5
```

Expected: compilation error — `PdfCoordinateMapper` not found.

- [ ] **Step 3: Create PdfCoordinateMapper**

Create `src/PdfReaderApp/Core/PdfCoordinateMapper.cs`:

```csharp
namespace PdfReaderApp.Core;

public sealed class PdfCoordinateMapper
{
    private readonly float _pageHeightPt;
    private readonly float _pixelsPerPoint;

    public PdfCoordinateMapper(float pageHeightPt, float scale, int dpi)
    {
        _pageHeightPt = pageHeightPt;
        _pixelsPerPoint = scale * (dpi / 72f);
    }

    // PDF user-space (bottom-left origin, points) -> render-space (top-left origin, pixels)
    public (float x, float y) PdfPointToRender(float pdfX, float pdfY)
    {
        float renderX = pdfX * _pixelsPerPoint;
        float renderY = (_pageHeightPt - pdfY) * _pixelsPerPoint;
        return (renderX, renderY);
    }

    // Render-space (top-left origin, pixels) -> PDF user-space (bottom-left origin, points)
    public (float x, float y) RenderPointToPdf(float renderX, float renderY)
    {
        float pdfX = renderX / _pixelsPerPoint;
        float pdfY = _pageHeightPt - renderY / _pixelsPerPoint;
        return (pdfX, pdfY);
    }
}
```

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~PdfCoordinateMapperTests" -v normal
```

Expected: 6 tests pass, 0 fail.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Core/PdfCoordinateMapper.cs \
        tests/PdfReaderApp.Tests/Core/PdfCoordinateMapperTests.cs
git commit -m "feat: add PdfCoordinateMapper with coordinate conversion tests"
```

---

### Task 3: IPdfDocumentService interface and ITextPdfDocumentService

**Files:**
- Create: `src/PdfReaderApp/Services/IPdfDocumentService.cs`
- Create: `src/PdfReaderApp/Services/ITextPdfDocumentService.cs`
- Create: `tests/PdfReaderApp.Tests/Services/ITextPdfDocumentServiceTests.cs`

**Interfaces:**
- Consumes: `TextBlock` from Task 1
- Produces:
  - `IPdfDocumentService.LoadFile(string filePath): void`
  - `IPdfDocumentService.ExtractStructure(): List<TextBlock>`
  - `IPdfDocumentService : IDisposable`

- [ ] **Step 1: Create IPdfDocumentService interface**

Create `src/PdfReaderApp/Services/IPdfDocumentService.cs`:

```csharp
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IPdfDocumentService : IDisposable
{
    void LoadFile(string filePath);
    List<TextBlock> ExtractStructure();
}
```

- [ ] **Step 2: Write failing integration tests**

Create `tests/PdfReaderApp.Tests/Services/ITextPdfDocumentServiceTests.cs`:

```csharp
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class ITextPdfDocumentServiceTests : IDisposable
{
    private readonly string _tempPdf;

    public ITextPdfDocumentServiceTests()
    {
        _tempPdf = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        CreateTestPdf(_tempPdf, "Hello World", "Second line of text");
    }

    private static void CreateTestPdf(string path, params string[] lines)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        using var doc = new Document(pdfDoc);
        foreach (var line in lines)
            doc.Add(new Paragraph(line));
    }

    [Fact]
    public void ExtractStructure_KnownPdf_ReturnsNonEmptyBlocks()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void ExtractStructure_KnownPdf_ContainsExpectedText()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();
        var allText = string.Join(" ", blocks.Select(b => b.Text));

        Assert.Contains("Hello World", allText);
    }

    [Fact]
    public void ExtractStructure_KnownPdf_BlocksHaveCorrectPageIndex()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.All(blocks, b => Assert.Equal(0, b.PageIndex));
    }

    [Fact]
    public void ExtractStructure_KnownPdf_BlocksHaveNonNegativeCoordinates()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.All(blocks, b =>
        {
            Assert.True(b.PdfX >= 0f, $"PdfX={b.PdfX} should be >= 0");
            Assert.True(b.PdfY >= 0f, $"PdfY={b.PdfY} should be >= 0");
        });
    }

    [Fact]
    public void ExtractStructure_BeforeLoadFile_ThrowsInvalidOperationException()
    {
        using var service = new ITextPdfDocumentService();

        Assert.Throws<InvalidOperationException>(() => service.ExtractStructure());
    }

    public void Dispose()
    {
        if (File.Exists(_tempPdf)) File.Delete(_tempPdf);
    }
}
```

- [ ] **Step 3: Run tests — verify they FAIL (class not yet created)**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: compilation error — `ITextPdfDocumentService` not found.

- [ ] **Step 4: Create ITextPdfDocumentService**

Create `src/PdfReaderApp/Services/ITextPdfDocumentService.cs`:

```csharp
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class ITextPdfDocumentService : IPdfDocumentService
{
    private PdfDocument? _pdfDoc;
    private PdfReader? _pdfReader;

    public void LoadFile(string filePath)
    {
        _pdfDoc?.Close();
        _pdfReader?.Close();

        _pdfReader = new PdfReader(filePath);
        _pdfDoc = new PdfDocument(_pdfReader);
    }

    public List<TextBlock> ExtractStructure()
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before ExtractStructure.");

        var blocks = new List<TextBlock>();

        for (int pageIndex = 0; pageIndex < _pdfDoc.GetNumberOfPages(); pageIndex++)
        {
            var page = _pdfDoc.GetPage(pageIndex + 1); // iText pages are 1-indexed
            var listener = new TextItemListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            foreach (var item in listener.Items)
            {
                blocks.Add(new TextBlock(
                    Text: item.Text,
                    PdfX: item.X,
                    PdfY: item.Y,
                    Width: item.Width,
                    Height: item.FontSize,
                    FontSize: item.FontSize,
                    PageIndex: pageIndex,
                    StructureType: ClassifyStructure(item.Text, item.FontSize)));
            }
        }

        return blocks;
    }

    private static string ClassifyStructure(string text, float fontSize)
    {
        if (fontSize >= 14f) return "Heading";
        if (text.TrimStart() is { } t && (t.StartsWith('•') || t.StartsWith('-'))) return "List";
        return "Paragraph";
    }

    public void Dispose()
    {
        _pdfDoc?.Close();
        _pdfReader?.Close();
        _pdfDoc = null;
        _pdfReader = null;
    }

    private sealed class TextItemListener : IEventListener
    {
        public List<RawTextItem> Items { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (data is not TextRenderInfo renderInfo) return;

            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var baseline = renderInfo.GetBaseline();
            var startPt = baseline.GetStartPoint();
            float x = startPt.Get(0); // index 0 = X in PDF user-space
            float y = startPt.Get(1); // index 1 = Y in PDF user-space
            float width = baseline.GetEndPoint().Get(0) - x;
            float fontSize = renderInfo.GetFontSize();

            Items.Add(new RawTextItem(text, x, y, width, fontSize));
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    private sealed record RawTextItem(string Text, float X, float Y, float Width, float FontSize);
}
```

- [ ] **Step 5: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~ITextPdfDocumentServiceTests" -v normal
```

Expected: 5 tests pass, 0 fail.

> If you see iText type-not-found errors in the test project, add `<PackageReference Include="itext" Version="8.0.5" />` to `tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj` and re-run.

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Services/IPdfDocumentService.cs \
        src/PdfReaderApp/Services/ITextPdfDocumentService.cs \
        tests/PdfReaderApp.Tests/Services/ITextPdfDocumentServiceTests.cs
git commit -m "feat: implement IPdfDocumentService facade and iText extraction (Phase 1)"
```

---

### Task 4: Wire IPdfDocumentService into MainViewModel and PdfStructureAnalyzer

**Files:**
- Modify: `src/PdfReaderApp/Services/PdfStructureAnalyzer.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IPdfDocumentService` from Task 3, `TextBlock` from Task 1
- The `PdfStructureAnalyzer(PdfDocument)` constructor is replaced with `PdfStructureAnalyzer(IPdfDocumentService)`. Callers: only `MainViewModel`.

- [ ] **Step 1: Verify there are no other callers of PdfStructureAnalyzer before refactoring**

```bash
grep -rn "PdfStructureAnalyzer" src/ --include="*.cs"
```

Expected: only `MainViewModel.cs` instantiates it. If additional callers exist, update them too before continuing.

- [ ] **Step 2: Replace PdfStructureAnalyzer**

Replace `src/PdfReaderApp/Services/PdfStructureAnalyzer.cs` entirely:

```csharp
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public class PdfStructureAnalyzer
{
    // Kept for backward-compat; remove after all callers migrate to AnalyzeRich()
    public class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public int PageIndex { get; set; }
        public string StructureType { get; set; } = "Paragraph";
    }

    private readonly IPdfDocumentService _documentService;

    public PdfStructureAnalyzer(IPdfDocumentService documentService)
    {
        _documentService = documentService;
    }

    public List<TextChunk> Analyze() =>
        _documentService.ExtractStructure()
            .Select(b => new TextChunk
            {
                Text = b.Text,
                PageIndex = b.PageIndex,
                StructureType = b.StructureType
            })
            .ToList();

    public List<TextBlock> AnalyzeRich() => _documentService.ExtractStructure();
}
```

- [ ] **Step 3: Replace MainViewModel**

Replace `src/PdfReaderApp/ViewModels/MainViewModel.cs` entirely:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AIService _aiService = new();
    private readonly IPdfDocumentService _documentService;
    private readonly PdfStructureAnalyzer _analyzer;

    [ObservableProperty]
    private string windowTitle = "Ultimate PDF Reader & Editor";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private string _chatInput = string.Empty;

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    public MainViewModel() : this(new ITextPdfDocumentService()) { }

    public MainViewModel(IPdfDocumentService documentService)
    {
        _documentService = documentService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?" });
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        FilePath = dialog.FileName;
        _documentService.LoadFile(FilePath);
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        string question = ChatInput;
        ChatInput = string.Empty;

        ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

        string context = BuildContextFromDocument();
        var response = await _aiService.AskQuestionAsync(question, context);
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = response });
    }

    private string BuildContextFromDocument()
    {
        if (string.IsNullOrEmpty(FilePath)) return string.Empty;

        try
        {
            var chunks = _analyzer.Analyze();
            return string.Join("\n", chunks.Take(50).Select(c => c.Text));
        }
        catch
        {
            return string.Empty;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) CurrentPage++;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) CurrentPage--;
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel += 0.2;

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.4) ZoomLevel -= 0.2;
    }

    public void Dispose() => _documentService.Dispose();
}

public class ChatMessage
{
    public string Role { get; set; } = "User";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
```

- [ ] **Step 4: Build the full solution and run all tests**

```bash
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
dotnet test tests/PdfReaderApp.Tests -v normal
```

Expected: 0 build errors. All tests pass (coordinate mapper 6 + service extraction 5 = 11 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/PdfStructureAnalyzer.cs \
        src/PdfReaderApp/ViewModels/MainViewModel.cs
git commit -m "feat: wire IPdfDocumentService into MainViewModel and PdfStructureAnalyzer"
```

---

## Done

After Task 4 commit, Phase 1 is complete:
- iText 8 extraction replaces PDFium `Split('\n')` heuristic
- `IPdfDocumentService` facade is ready for Phase 2 (annotation write path)
- `PdfCoordinateMapper` is ready for Phase 2 annotation coordinate mapping
- 11 tests covering coordinate math and extraction
