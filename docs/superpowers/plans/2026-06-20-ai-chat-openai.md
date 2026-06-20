# OpenAI Chat Integration (Sub-project 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `AIService` with a real OpenAI-backed chat that streams tokens, remembers the session conversation, manages the API key securely (DPAPI), and uses text around the current PDF page as context.

**Architecture:** App-level `AiChatService` builds the message list and streams via `Microsoft.Extensions.AI.IChatClient`. The concrete client is created by `IChatClientFactory` (OpenAI provider) from the stored API key, so swapping providers or rotating the key needs no restart. `ISettingsService` stores the key encrypted with Windows DPAPI. The `MainViewModel` (instantiated by XAML — its parameterless constructor is the composition root) wires everything; a `SettingsWindow` captures the key.

**Tech Stack:** Microsoft.Extensions.AI (+ .OpenAI provider) + OpenAI SDK, CommunityToolkit.Mvvm, MaterialDesignThemes, WPF .NET 10, xUnit.

## Global Constraints

- Target framework: `net10.0-windows`; Nullable: enabled
- No `Co-Authored-By` trailer in any commit message; no `--no-verify`
- Do NOT add or commit `conductor/` or `.serena/`
- Never hardcode the API key in source; read only from `ISettingsService`
- Default chat model: `gpt-4o-mini`
- Context window around current page: ±2 pages; context char cap: 48000
- Mid-stream interruption sentinel (verbatim): ` ...[lỗi: phản hồi bị gián đoạn]`
- **M.E.AI version note:** Microsoft.Extensions.AI member shapes (`ChatResponseUpdate` constructor, the exact `IChatClient` member set, `AsIChatClient` vs `AsChatClient`) vary across the 9.x packages. This plan's code targets the 9.x line. If a member name differs in the restored package, use the IntelliSense equivalent — a build and the unit tests will surface any mismatch immediately. The mismatch risk is confined to `OpenAiChatClientFactory` and the fake `IChatClient` in tests; `AiChatService` logic does not depend on it.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/PdfReaderApp/PdfReaderApp.csproj` | Modify | Add Microsoft.Extensions.AI, .OpenAI, OpenAI packages |
| `src/PdfReaderApp/Services/AiChatError.cs` | Create | Error kind enum + `AiChatException` |
| `src/PdfReaderApp/Services/AiErrorClassifier.cs` | Create | Pure: map exception/status → `AiChatError` |
| `src/PdfReaderApp/Services/ISettingsService.cs` | Create | API-key storage contract |
| `src/PdfReaderApp/Services/WindowsSettingsService.cs` | Create | DPAPI-encrypted key file in %APPDATA% |
| `src/PdfReaderApp/Services/IChatClientFactory.cs` | Create | Build `IChatClient` from an API key |
| `src/PdfReaderApp/Services/AiChatService.cs` | Create | Message building + streaming + history + error handling |
| `src/PdfReaderApp/Services/OpenAiChatClientFactory.cs` | Create | Concrete OpenAI-backed `IChatClientFactory` |
| `src/PdfReaderApp/Services/DocumentContextBuilder.cs` | Create | Pure: text around current page |
| `src/PdfReaderApp/Services/AIService.cs` | Delete | Replaced by `AiChatService` |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | Modify | Inject services; streaming SendMessage; OpenSettings; observable ChatMessage |
| `src/PdfReaderApp/SettingsWindow.xaml(.cs)` | Create | API-key entry dialog |
| `src/PdfReaderApp/MainWindow.xaml` | Modify | Bind Settings toolbar button to OpenSettingsCommand |
| `tests/PdfReaderApp.Tests/Services/AiErrorClassifierTests.cs` | Create | Classifier tests |
| `tests/PdfReaderApp.Tests/Services/WindowsSettingsServiceTests.cs` | Create | DPAPI round-trip tests |
| `tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs` | Create | Message/stream/history/error tests (fake IChatClient) |
| `tests/PdfReaderApp.Tests/Services/DocumentContextBuilderTests.cs` | Create | Context windowing tests |

---

### Task 1: Dependencies + error model + classifier

**Files:**
- Modify: `src/PdfReaderApp/PdfReaderApp.csproj`
- Create: `src/PdfReaderApp/Services/AiChatError.cs`
- Create: `src/PdfReaderApp/Services/AiErrorClassifier.cs`
- Test: `tests/PdfReaderApp.Tests/Services/AiErrorClassifierTests.cs`

**Interfaces:**
- Produces:
  - `enum AiChatError { Unauthorized, RateLimit, Network, Unknown }`
  - `class AiChatException : Exception { AiChatError Error { get; } }` with ctor `(AiChatError error, string message, Exception? inner = null)`
  - `static AiChatError AiErrorClassifier.ClassifyStatus(int status)`
  - `static AiChatError AiErrorClassifier.Classify(Exception ex)`

- [ ] **Step 1: Add packages**

Run (lets NuGet resolve compatible versions; .OpenAI is prerelease):

```bash
cd src/PdfReaderApp
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI.OpenAI --prerelease
dotnet add package OpenAI
cd ../..
```

Expected: three `<PackageReference>` lines added to `PdfReaderApp.csproj`; restore succeeds.

- [ ] **Step 2: Write the failing classifier test**

Create `tests/PdfReaderApp.Tests/Services/AiErrorClassifierTests.cs`:

```csharp
using System.Net.Http;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiErrorClassifierTests
{
    [Theory]
    [InlineData(401, AiChatError.Unauthorized)]
    [InlineData(429, AiChatError.RateLimit)]
    [InlineData(500, AiChatError.Unknown)]
    [InlineData(404, AiChatError.Unknown)]
    public void ClassifyStatus_MapsHttpStatusToError(int status, AiChatError expected)
    {
        Assert.Equal(expected, AiErrorClassifier.ClassifyStatus(status));
    }

    [Fact]
    public void Classify_HttpRequestException_IsNetwork()
    {
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(new HttpRequestException("boom")));
    }

    [Fact]
    public void Classify_TaskCanceled_IsNetwork()
    {
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(new TaskCanceledException()));
    }

    [Fact]
    public void Classify_GenericException_IsUnknown()
    {
        Assert.Equal(AiChatError.Unknown, AiErrorClassifier.Classify(new InvalidOperationException()));
    }
}
```

- [ ] **Step 3: Run test — verify it FAILS (types not defined)**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: compile errors — `AiChatError` / `AiErrorClassifier` not found.

- [ ] **Step 4: Create the error model**

Create `src/PdfReaderApp/Services/AiChatError.cs`:

```csharp
namespace PdfReaderApp.Services;

public enum AiChatError
{
    Unauthorized,
    RateLimit,
    Network,
    Unknown
}

public sealed class AiChatException : Exception
{
    public AiChatError Error { get; }

    public AiChatException(AiChatError error, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
    }
}
```

- [ ] **Step 5: Create the classifier**

Create `src/PdfReaderApp/Services/AiErrorClassifier.cs`:

```csharp
using System.Net.Http;

namespace PdfReaderApp.Services;

public static class AiErrorClassifier
{
    public static AiChatError ClassifyStatus(int status) => status switch
    {
        401 => AiChatError.Unauthorized,
        429 => AiChatError.RateLimit,
        _ => AiChatError.Unknown
    };

    public static AiChatError Classify(Exception ex)
    {
        // OpenAI SDK surfaces HTTP failures as System.ClientModel.ClientResultException with a Status.
        if (ex is System.ClientModel.ClientResultException cre)
            return ClassifyStatus(cre.Status);

        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return AiChatError.Network;

        return AiChatError.Unknown;
    }
}
```

- [ ] **Step 6: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~AiErrorClassifierTests" -v normal
```

Expected: 7 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/PdfReaderApp.csproj \
        src/PdfReaderApp/Services/AiChatError.cs \
        src/PdfReaderApp/Services/AiErrorClassifier.cs \
        tests/PdfReaderApp.Tests/Services/AiErrorClassifierTests.cs
git commit -m "feat: add AI chat dependencies, error model, and error classifier"
```

---

### Task 2: Settings service (DPAPI key storage)

**Files:**
- Create: `src/PdfReaderApp/Services/ISettingsService.cs`
- Create: `src/PdfReaderApp/Services/WindowsSettingsService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/WindowsSettingsServiceTests.cs`

**Interfaces:**
- Produces:
  - `interface ISettingsService { string? GetApiKey(); void SaveApiKey(string apiKey); bool HasApiKey(); }`
  - `WindowsSettingsService` with ctor `WindowsSettingsService(string? storageDirectory = null)` (directory override is for tests; default = `%APPDATA%\PdfReaderApp`)

- [ ] **Step 1: Create the interface**

Create `src/PdfReaderApp/Services/ISettingsService.cs`:

```csharp
namespace PdfReaderApp.Services;

public interface ISettingsService
{
    string? GetApiKey();
    void SaveApiKey(string apiKey);
    bool HasApiKey();
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/WindowsSettingsServiceTests.cs`:

```csharp
using System.IO;
using System.Text;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class WindowsSettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public WindowsSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void SaveThenGet_ReturnsSameKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-test-12345");

        Assert.Equal("sk-test-12345", svc.GetApiKey());
    }

    [Fact]
    public void HasApiKey_FalseWhenNothingSaved()
    {
        var svc = new WindowsSettingsService(_dir);
        Assert.False(svc.HasApiKey());
    }

    [Fact]
    public void HasApiKey_TrueAfterSave()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-test-12345");
        Assert.True(svc.HasApiKey());
    }

    [Fact]
    public void StoredFile_DoesNotContainPlaintextKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-secret-PLAINTEXT");

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "settings.dat"));
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("sk-secret-PLAINTEXT", asText);
    }

    [Fact]
    public void GetApiKey_ReturnsNullWhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.dat"), "not-valid-dpapi-bytes");
        var svc = new WindowsSettingsService(_dir);

        Assert.Null(svc.GetApiKey());
        Assert.False(svc.HasApiKey());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 3: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `WindowsSettingsService` not found.

- [ ] **Step 4: Implement the service**

Create `src/PdfReaderApp/Services/WindowsSettingsService.cs`:

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PdfReaderApp.Services;

public sealed class WindowsSettingsService : ISettingsService
{
    private readonly string _filePath;

    public WindowsSettingsService(string? storageDirectory = null)
    {
        string dir = storageDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PdfReaderApp");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.dat");
    }

    public string? GetApiKey()
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(_filePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (CryptographicException)
        {
            return null; // corrupt or written by a different user — degrade to "no key"
        }
    }

    public void SaveApiKey(string apiKey)
    {
        byte[] plain = Encoding.UTF8.GetBytes(apiKey);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }

    public bool HasApiKey() => !string.IsNullOrEmpty(GetApiKey());
}
```

- [ ] **Step 5: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~WindowsSettingsServiceTests" -v normal
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Services/ISettingsService.cs \
        src/PdfReaderApp/Services/WindowsSettingsService.cs \
        tests/PdfReaderApp.Tests/Services/WindowsSettingsServiceTests.cs
git commit -m "feat: add DPAPI-encrypted settings service for API key storage"
```

---

### Task 3: AiChatService (message building, streaming, history, errors)

**Files:**
- Create: `src/PdfReaderApp/Services/IChatClientFactory.cs`
- Create: `src/PdfReaderApp/Services/AiChatService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs`

**Interfaces:**
- Consumes: `ISettingsService` (Task 2), `AiChatException`/`AiChatError`/`AiErrorClassifier` (Task 1), `Microsoft.Extensions.AI` (`IChatClient`, `ChatMessage`, `ChatRole`, `ChatResponseUpdate`, `ChatOptions`)
- Produces:
  - `interface IChatClientFactory { Microsoft.Extensions.AI.IChatClient Create(string apiKey); }`
  - `class AiChatService` with:
    - ctor `AiChatService(ISettingsService settings, IChatClientFactory chatClientFactory)`
    - `bool IsConfigured { get; }` (true when settings has a key)
    - `IAsyncEnumerable<string> AskStreamingAsync(string question, string documentContext, CancellationToken ct = default)`
    - `void ResetConversation()`

**Background — streaming + iterator rules:** C# forbids `yield return` inside a `try` that has a `catch`, and inside `catch`/`finally`. The implementation below pulls each item inside a try, records the outcome in locals, then yields *after* the try/catch. The mid-stream interruption appends the sentinel and stops; a failure before any token throws `AiChatException`.

- [ ] **Step 1: Create the factory interface**

Create `src/PdfReaderApp/Services/IChatClientFactory.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public interface IChatClientFactory
{
    IChatClient Create(string apiKey);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiChatServiceTests
{
    // ---- Fakes ----
    private sealed class FakeSettings : ISettingsService
    {
        private string? _key;
        public FakeSettings(string? key) => _key = key;
        public string? GetApiKey() => _key;
        public void SaveApiKey(string apiKey) => _key = apiKey;
        public bool HasApiKey() => !string.IsNullOrEmpty(_key);
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string[] _tokens;
        private readonly bool _throwAfterTokens;
        public List<ChatMessage> LastMessages { get; private set; } = new();

        public FakeChatClient(string[] tokens, bool throwAfterTokens = false)
        {
            _tokens = tokens;
            _throwAfterTokens = throwAfterTokens;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            foreach (var t in _tokens)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, t);
            }
            if (_throwAfterTokens)
                throw new InvalidOperationException("stream broke");
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeFactory : IChatClientFactory
    {
        private readonly IChatClient _client;
        public FakeFactory(IChatClient client) => _client = client;
        public IChatClient Create(string apiKey) => _client;
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> stream)
    {
        var list = new List<string>();
        await foreach (var s in stream) list.Add(s);
        return list;
    }

    // ---- Tests ----

    [Fact]
    public async Task AskStreaming_YieldsTokensInOrder()
    {
        var client = new FakeChatClient(new[] { "Xin ", "chào", "!" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ngữ cảnh"));

        Assert.Equal(new[] { "Xin ", "chào", "!" }, tokens);
    }

    [Fact]
    public async Task AskStreaming_SendsSystemContextAndQuestion()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Câu hỏi?", "Nội dung tài liệu ABC"));

        var roles = client.LastMessages.Select(m => m.Role).ToList();
        Assert.Equal(ChatRole.System, roles.First());
        var joined = string.Join("\n", client.LastMessages.Select(m => m.Text));
        Assert.Contains("Nội dung tài liệu ABC", joined);
        Assert.Equal(ChatRole.User, client.LastMessages.Last().Role);
        Assert.Contains("Câu hỏi?", client.LastMessages.Last().Text);
    }

    [Fact]
    public async Task AskStreaming_SecondTurn_IncludesFirstTurnHistory()
    {
        var client = new FakeChatClient(new[] { "Trả lời 1" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Trả lời 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }

    [Fact]
    public async Task AskStreaming_MidStreamError_AppendsSentinelAndStops()
    {
        var client = new FakeChatClient(new[] { "phần ", "đầu" }, throwAfterTokens: true);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ctx"));

        Assert.Equal("phần ", tokens[0]);
        Assert.Equal("đầu", tokens[1]);
        Assert.Equal(" ...[lỗi: phản hồi bị gián đoạn]", tokens[2]);
    }

    [Fact]
    public async Task AskStreaming_NoApiKey_ThrowsUnauthorized()
    {
        var client = new FakeChatClient(new[] { "x" });
        var svc = new AiChatService(new FakeSettings(null), new FakeFactory(client));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            async () => await Collect(svc.AskStreamingAsync("hi", "ctx")));
        Assert.Equal(AiChatError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task ResetConversation_DropsHistory()
    {
        var client = new FakeChatClient(new[] { "a" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        svc.ResetConversation();
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }
}
```

- [ ] **Step 3: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `AiChatService` not found. (If the fake fails to compile because `IChatClient` has a member the fake lacks, add that member per IntelliSense — see version note.)

- [ ] **Step 4: Implement AiChatService**

Create `src/PdfReaderApp/Services/AiChatService.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public sealed class AiChatService
{
    private const string SystemPrompt =
        "Bạn là trợ lý đọc tài liệu PDF. Trả lời bằng tiếng Việt, ngắn gọn, " +
        "chỉ dựa trên nội dung tài liệu được cung cấp. Nếu chưa có tài liệu hoặc " +
        "nội dung không đủ, hãy nói rõ điều đó.";
    private const string InterruptedSentinel = " ...[lỗi: phản hồi bị gián đoạn]";
    private const int MaxContextChars = 48000;

    private readonly ISettingsService _settings;
    private readonly IChatClientFactory _factory;
    private readonly List<ChatMessage> _history = new();

    private IChatClient? _client;
    private string? _clientKey;

    public AiChatService(ISettingsService settings, IChatClientFactory chatClientFactory)
    {
        _settings = settings;
        _factory = chatClientFactory;
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public bool IsConfigured => _settings.HasApiKey();

    public void ResetConversation()
    {
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question, string documentContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key))
            throw new AiChatException(AiChatError.Unauthorized, "Chưa cấu hình API key.");

        if (_client is null || _clientKey != key)
        {
            _client = _factory.Create(key);
            _clientKey = key;
        }

        string context = documentContext.Length > MaxContextChars
            ? documentContext[..MaxContextChars]
            : documentContext;
        _history.Add(new ChatMessage(ChatRole.User,
            $"Ngữ cảnh tài liệu:\n{context}\n\nCâu hỏi: {question}"));

        var assistant = new StringBuilder();

        IAsyncEnumerator<ChatResponseUpdate> e;
        try
        {
            e = _client.GetStreamingResponseAsync(_history, options: null, ct)
                       .GetAsyncEnumerator(ct);
        }
        catch (Exception ex)
        {
            throw new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
        }

        try
        {
            while (true)
            {
                bool hasNext = false;
                string? text = null;
                bool interrupted = false;
                AiChatException? fatal = null;

                try
                {
                    hasNext = await e.MoveNextAsync();
                    if (hasNext) text = e.Current.Text;
                }
                catch (Exception ex)
                {
                    if (assistant.Length > 0) interrupted = true;
                    else fatal = new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
                }

                if (fatal != null)
                    throw fatal;

                if (interrupted)
                {
                    assistant.Append(InterruptedSentinel);
                    _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
                    yield return InterruptedSentinel;
                    yield break;
                }

                if (!hasNext)
                    break;

                if (!string.IsNullOrEmpty(text))
                {
                    assistant.Append(text);
                    yield return text;
                }
            }
        }
        finally
        {
            await e.DisposeAsync();
        }

        _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
    }
}
```

- [ ] **Step 5: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~AiChatServiceTests" -v normal
```

Expected: 6 tests pass. (If `ChatResponseUpdate`/`IChatClient` members differ, adjust the fake and the `_client.GetStreamingResponseAsync(...)` call per IntelliSense — see version note. The yield/try/finally structure must stay as written.)

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Services/IChatClientFactory.cs \
        src/PdfReaderApp/Services/AiChatService.cs \
        tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs
git commit -m "feat: add AiChatService with streaming, history, and error handling"
```

---

### Task 4: DocumentContextBuilder (text around current page)

**Files:**
- Create: `src/PdfReaderApp/Services/DocumentContextBuilder.cs`
- Test: `tests/PdfReaderApp.Tests/Services/DocumentContextBuilderTests.cs`

**Interfaces:**
- Consumes: `PdfReaderApp.Models.TextBlock` (from iText Phase 1: positional record with `Text` and 0-based `PageIndex`)
- Produces: `static string DocumentContextBuilder.BuildAround(IReadOnlyList<TextBlock> blocks, int currentPageOneBased, int window, int maxChars = 48000)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/DocumentContextBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentContextBuilderTests
{
    private static TextBlock Block(string text, int pageIndex) =>
        new(text, 0f, 0f, 0f, 0f, 12f, pageIndex, "Paragraph");

    private static List<TextBlock> FivePages() => new()
    {
        Block("page0", 0), Block("page1", 1), Block("page2", 2),
        Block("page3", 3), Block("page4", 4)
    };

    [Fact]
    public void BuildAround_IncludesWindowPagesOnly()
    {
        // currentPage=3 (1-based) -> index 2; window 1 -> indices 1,2,3
        var ctx = DocumentContextBuilder.BuildAround(FivePages(), currentPageOneBased: 3, window: 1);

        Assert.Contains("page1", ctx);
        Assert.Contains("page2", ctx);
        Assert.Contains("page3", ctx);
        Assert.DoesNotContain("page0", ctx);
        Assert.DoesNotContain("page4", ctx);
    }

    [Fact]
    public void BuildAround_FirstPage_DoesNotFailOnNegativeLowerBound()
    {
        var ctx = DocumentContextBuilder.BuildAround(FivePages(), currentPageOneBased: 1, window: 2);

        Assert.Contains("page0", ctx);
        Assert.Contains("page2", ctx);
        Assert.DoesNotContain("page3", ctx);
    }

    [Fact]
    public void BuildAround_EmptyBlocks_ReturnsEmptyString()
    {
        var ctx = DocumentContextBuilder.BuildAround(new List<TextBlock>(), currentPageOneBased: 1, window: 2);
        Assert.Equal(string.Empty, ctx);
    }

    [Fact]
    public void BuildAround_TruncatesToMaxChars()
    {
        var blocks = new List<TextBlock> { Block(new string('x', 100), 0) };
        var ctx = DocumentContextBuilder.BuildAround(blocks, currentPageOneBased: 1, window: 0, maxChars: 10);
        Assert.True(ctx.Length <= 10);
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `DocumentContextBuilder` not found.

- [ ] **Step 3: Implement the builder**

Create `src/PdfReaderApp/Services/DocumentContextBuilder.cs`:

```csharp
using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class DocumentContextBuilder
{
    public static string BuildAround(
        IReadOnlyList<TextBlock> blocks, int currentPageOneBased, int window, int maxChars = 48000)
    {
        int currentIndex = currentPageOneBased - 1;
        int low = currentIndex - window;
        int high = currentIndex + window;

        var sb = new StringBuilder();
        foreach (var b in blocks)
        {
            if (b.PageIndex < low || b.PageIndex > high) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(b.Text);
            if (sb.Length >= maxChars) break;
        }

        string result = sb.ToString();
        return result.Length > maxChars ? result[..maxChars] : result;
    }
}
```

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~DocumentContextBuilderTests" -v normal
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/DocumentContextBuilder.cs \
        tests/PdfReaderApp.Tests/Services/DocumentContextBuilderTests.cs
git commit -m "feat: add DocumentContextBuilder for current-page context window"
```

---

### Task 5: OpenAI factory + MainViewModel streaming integration

**Files:**
- Create: `src/PdfReaderApp/Services/OpenAiChatClientFactory.cs`
- Delete: `src/PdfReaderApp/Services/AIService.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs` (full replacement below)

**Interfaces:**
- Consumes: `AiChatService`, `ISettingsService`, `IChatClientFactory`, `WindowsSettingsService`, `DocumentContextBuilder`, `PdfStructureAnalyzer.AnalyzeRich()` → `List<TextBlock>`, `IPdfDocumentService`
- Produces:
  - `OpenAiChatClientFactory : IChatClientFactory`
  - `MainViewModel` ctors: parameterless (composition root) and `MainViewModel(IPdfDocumentService, ISettingsService, IChatClientFactory)`
  - Observable `ChatMessage` (streaming-friendly `Content`)

**Note:** This task has no unit tests — it is WPF/composition glue. It ends with a build and a manual smoke check. `OpenSettingsCommand` shows a placeholder message here; Task 6 replaces it with the real dialog.

- [ ] **Step 1: Create the OpenAI factory**

Create `src/PdfReaderApp/Services/OpenAiChatClientFactory.cs`:

```csharp
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace PdfReaderApp.Services;

public sealed class OpenAiChatClientFactory : IChatClientFactory
{
    private const string Model = "gpt-4o-mini";

    public IChatClient Create(string apiKey) =>
        new ChatClient(Model, apiKey).AsIChatClient();
}
```

> If the restored `Microsoft.Extensions.AI.OpenAI` exposes `AsChatClient()` instead of `AsIChatClient()`, use that. A build error here is the signal.

- [ ] **Step 2: Delete the old placeholder**

```bash
git rm src/PdfReaderApp/Services/AIService.cs
```

- [ ] **Step 3: Replace MainViewModel**

Replace `src/PdfReaderApp/ViewModels/MainViewModel.cs` entirely:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int ContextPageWindow = 2;

    private readonly IPdfDocumentService _documentService;
    private readonly ISettingsService _settingsService;
    private readonly PdfStructureAnalyzer _analyzer;
    private readonly AiChatService _chatService;

    private List<TextBlock> _documentBlocks = new();

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

    public MainViewModel()
        : this(new ITextPdfDocumentService(), new WindowsSettingsService(), new OpenAiChatClientFactory()) { }

    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        _chatService = new AiChatService(settingsService, chatClientFactory);

        ChatMessages.Add(new ChatMessage
        {
            Role = "AI",
            Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
        });
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
        try
        {
            _documentService.LoadFile(FilePath);
            _documentBlocks = _analyzer.AnalyzeRich();
            _chatService.ResetConversation();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            System.Windows.MessageBox.Show(
                $"Không thể mở file PDF: {ex.Message}",
                "Lỗi mở file",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            FilePath = null;
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        string question = ChatInput;
        ChatInput = string.Empty;
        ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

        if (!_chatService.IsConfigured)
        {
            ChatMessages.Add(new ChatMessage
            {
                Role = "AI",
                Content = "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key."
            });
            return;
        }

        string context = DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);

        var aiMessage = new ChatMessage { Role = "AI", Content = string.Empty };
        ChatMessages.Add(aiMessage);

        try
        {
            await foreach (var token in _chatService.AskStreamingAsync(question, context))
            {
                aiMessage.Content += token;
            }
        }
        catch (AiChatException ex)
        {
            aiMessage.Content = MapError(ex.Error);
        }
        catch (Exception)
        {
            aiMessage.Content = "Đã xảy ra lỗi không xác định khi gọi AI.";
        }
    }

    private static string MapError(AiChatError error) => error switch
    {
        AiChatError.Unauthorized => "API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt.",
        AiChatError.RateLimit => "Đã vượt giới hạn yêu cầu, vui lòng thử lại sau.",
        AiChatError.Network => "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.",
        _ => "Đã xảy ra lỗi khi gọi AI."
    };

    [RelayCommand]
    private void OpenSettings()
    {
        // Replaced by the real dialog in Task 6.
        System.Windows.MessageBox.Show("Cài đặt API key (sẽ hoàn thiện ở bước kế tiếp).");
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

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = "User";

    [ObservableProperty]
    private string _content = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;
}
```

> `ChatMessage.Content` is now an `[ObservableProperty]`, so the bound `TextBlock` updates as tokens append during streaming. `SendMessage` keeps the `await` on the UI context (no `ConfigureAwait(false)`), so `aiMessage.Content += token` runs on the UI thread — no manual Dispatcher needed.

- [ ] **Step 4: Build and run all existing tests**

```bash
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
dotnet test tests/PdfReaderApp.Tests -v normal
```

Expected: 0 build errors. All prior tests still pass (Phase 1 + Tasks 1-4). The `XAML <viewmodels:MainViewModel />` still resolves via the parameterless constructor.

- [ ] **Step 5: Manual smoke check**

Run `dotnet run --project src/PdfReaderApp`. Open a PDF; type a question and Send. Without a key configured you should see the "Chưa cấu hình API key" message (not a crash).

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Services/OpenAiChatClientFactory.cs \
        src/PdfReaderApp/ViewModels/MainViewModel.cs
git commit -m "feat: wire AiChatService streaming into MainViewModel; remove placeholder AIService"
```

---

### Task 6: Settings dialog + toolbar wiring

**Files:**
- Create: `src/PdfReaderApp/SettingsWindow.xaml`
- Create: `src/PdfReaderApp/SettingsWindow.xaml.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs` (OpenSettings command body only)
- Modify: `src/PdfReaderApp/MainWindow.xaml:57-59` (Settings button → command)

**Interfaces:**
- Consumes: `ISettingsService` (Task 2), the existing `MainViewModel._settingsService` field (Task 5)
- Produces: `SettingsWindow` with `string? ApiKey` result property (null when cancelled)

**Note:** No unit tests — WPF view + dialog. Ends with build + manual smoke. Showing a window from the command matches the existing codebase pattern (OpenFile already opens `OpenFileDialog` from the VM).

- [ ] **Step 1: Create the SettingsWindow view**

Create `src/PdfReaderApp/SettingsWindow.xaml`:

```xml
<Window x:Class="PdfReaderApp.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        Title="Cài đặt AI" Height="220" Width="460"
        WindowStartupLocation="CenterOwner"
        TextElement.Foreground="{DynamicResource MaterialDesignBody}"
        Background="{DynamicResource MaterialDesignPaper}"
        FontFamily="{materialDesign:MaterialDesignFont}">
    <StackPanel Margin="24">
        <TextBlock Text="OpenAI API Key" FontWeight="Bold" Margin="0,0,0,8"/>
        <PasswordBox x:Name="ApiKeyBox"
                     materialDesign:HintAssist.Hint="Dán API key (để trống nếu giữ key hiện tại)"
                     Style="{StaticResource MaterialDesignFloatingHintPasswordBox}"/>
        <TextBlock Text="Key được mã hóa và lưu cục bộ (DPAPI)."
                   Opacity="0.6" FontSize="11" Margin="0,8,0,0"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,24,0,0">
            <Button x:Name="CancelButton" Content="HỦY" Width="90"
                    Style="{StaticResource MaterialDesignFlatButton}" Click="CancelButton_Click"/>
            <Button x:Name="SaveButton" Content="LƯU" Width="90" Margin="8,0,0,0"
                    Style="{StaticResource MaterialDesignRaisedButton}" Click="SaveButton_Click"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Create the SettingsWindow code-behind**

Create `src/PdfReaderApp/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;

namespace PdfReaderApp;

public partial class SettingsWindow : Window
{
    public string? ApiKey { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKey = ApiKeyBox.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKey = null;
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Replace the OpenSettings command body in MainViewModel**

In `src/PdfReaderApp/ViewModels/MainViewModel.cs`, replace the `OpenSettings` method body:

```csharp
    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ApiKey))
        {
            _settingsService.SaveApiKey(window.ApiKey.Trim());
        }
    }
```

> The next `AskStreamingAsync` call rebuilds the `IChatClient` from the new key (`AiChatService` compares the cached key), so the new key takes effect with no restart.

- [ ] **Step 4: Wire the toolbar Settings button**

In `src/PdfReaderApp/MainWindow.xaml`, the Settings button (lines 57-59) currently has no command. Replace it:

```xml
                    <Button Style="{StaticResource MaterialDesignFlatButton}" Foreground="White" Height="60" ToolTip="Settings"
                            Command="{Binding OpenSettingsCommand}">
                        <materialDesign:PackIcon Kind="Settings" Width="24" Height="24"/>
                    </Button>
```

- [ ] **Step 5: Build and run all tests**

```bash
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
dotnet test tests/PdfReaderApp.Tests -v normal
```

Expected: 0 build errors; all tests pass.

- [ ] **Step 6: Manual end-to-end smoke check**

Run `dotnet run --project src/PdfReaderApp`. Click the Settings button (left rail, bottom), paste a real OpenAI key, Save. Open a PDF, ask a question in Vietnamese — the answer should stream in token by token and reflect the current page's content. Ask a follow-up referring to the previous answer to confirm history works.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/SettingsWindow.xaml \
        src/PdfReaderApp/SettingsWindow.xaml.cs \
        src/PdfReaderApp/ViewModels/MainViewModel.cs \
        src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: add Settings dialog for API key and wire toolbar button"
```

---

## Done

After Task 6:
- Real OpenAI chat with token streaming, session history, and Vietnamese error messages
- API key stored encrypted (DPAPI) via a Settings dialog; key rotation needs no restart
- Context = text around the current page (±2), ready for Sub-project 2 (SQLite RAG) to replace via the unchanged `AskStreamingAsync(question, context)` boundary
- 22 new unit tests (7 classifier + 5 settings + 6 chat service + 4 context builder), plus all Phase 1 tests still green
