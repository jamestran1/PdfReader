using System.IO;
using System.Text;
using PdfReaderApp.Models;
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
        svc.SaveApiKey(AiProvider.OpenAi, "sk-test-12345");

        Assert.Equal("sk-test-12345", svc.GetApiKey(AiProvider.OpenAi));
    }

    [Fact]
    public void HasApiKey_FalseWhenNothingSaved()
    {
        var svc = new WindowsSettingsService(_dir);
        Assert.False(svc.HasApiKey(AiProvider.OpenAi));
    }

    [Fact]
    public void HasApiKey_TrueAfterSave()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.OpenAi, "sk-test-12345");
        Assert.True(svc.HasApiKey(AiProvider.OpenAi));
    }

    [Fact]
    public void StoredFile_DoesNotContainPlaintextKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.OpenAi, "sk-secret-PLAINTEXT");

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "settings.dat"));
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("sk-secret-PLAINTEXT", asText);
    }

    [Fact]
    public void GetApiKey_ReturnsNullWhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.dat"), "not-valid-dpapi-bytes");
        var svc = new WindowsSettingsService(_dir);

        Assert.Null(svc.GetApiKey(AiProvider.OpenAi));
        Assert.False(svc.HasApiKey(AiProvider.OpenAi));
    }

    [Fact]
    public void ApiKeys_AreStoredPerProvider()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.OpenAi, "sk-openai");
        svc.SaveApiKey(AiProvider.Gemini, "gm-key");
        svc.SaveApiKey(AiProvider.Claude, "cl-key");

        Assert.Equal("sk-openai", svc.GetApiKey(AiProvider.OpenAi));
        Assert.Equal("gm-key", svc.GetApiKey(AiProvider.Gemini));
        Assert.Equal("cl-key", svc.GetApiKey(AiProvider.Claude));
    }

    [Fact]
    public void ApiKeys_UseSeparateFiles_AndOpenAiKeepsLegacyFileName()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.OpenAi, "a");
        svc.SaveApiKey(AiProvider.Gemini, "b");
        svc.SaveApiKey(AiProvider.Claude, "c");

        Assert.True(File.Exists(Path.Combine(_dir, "settings.dat")));
        Assert.True(File.Exists(Path.Combine(_dir, "settings.gemini.dat")));
        Assert.True(File.Exists(Path.Combine(_dir, "settings.claude.dat")));
    }

    [Fact]
    public void HasApiKey_IsFalseForProvidersWithoutKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.Gemini, "gm-key");

        Assert.True(svc.HasApiKey(AiProvider.Gemini));
        Assert.False(svc.HasApiKey(AiProvider.OpenAi));
        Assert.False(svc.HasApiKey(AiProvider.Claude));
    }

    [Fact]
    public void SaveApiKey_EmptyString_DeletesTheKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.Claude, "cl-key");

        svc.SaveApiKey(AiProvider.Claude, "");

        Assert.False(svc.HasApiKey(AiProvider.Claude));
        Assert.Null(svc.GetApiKey(AiProvider.Claude));
    }

    [Fact]
    public void SaveApiKey_EmptyString_DoesNotThrow_WhenNoKeyExists()
    {
        var svc = new WindowsSettingsService(_dir);

        svc.SaveApiKey(AiProvider.Gemini, "");

        Assert.False(svc.HasApiKey(AiProvider.Gemini));
    }

    [Fact]
    public void GetModelSettings_ReturnsDefault_WhenNothingSaved()
    {
        var svc = new WindowsSettingsService(_dir);

        Assert.Equal(AiModelSettings.Default, svc.GetModelSettings());
    }

    [Fact]
    public void ModelSettings_RoundTrip()
    {
        var svc = new WindowsSettingsService(_dir);
        var settings = new AiModelSettings(AiProvider.Claude, "claude-sonnet-5-5", AiProvider.Gemini, "gemini-embedding-001");

        svc.SaveModelSettings(settings);

        Assert.Equal(settings, new WindowsSettingsService(_dir).GetModelSettings());
    }

    [Fact]
    public void GetModelSettings_ReturnsDefault_WhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "ai-models.json"), "{ không phải json");
        var svc = new WindowsSettingsService(_dir);

        Assert.Equal(AiModelSettings.Default, svc.GetModelSettings());
    }

    [Fact]
    public void GetModelSettings_ReturnsDefault_WhenModelNameIsMissing()
    {
        File.WriteAllText(Path.Combine(_dir, "ai-models.json"),
            """{"ChatProvider":"Gemini","ChatModel":"","EmbeddingProvider":"OpenAi","EmbeddingModel":"x"}""");
        var svc = new WindowsSettingsService(_dir);

        Assert.Equal(AiModelSettings.Default, svc.GetModelSettings());
    }

    [Fact]
    public void ModelSettings_FileDoesNotContainApiKeys()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey(AiProvider.OpenAi, "sk-secret-PLAINTEXT");
        svc.SaveModelSettings(AiModelSettings.Default);

        Assert.DoesNotContain("sk-secret-PLAINTEXT", File.ReadAllText(Path.Combine(_dir, "ai-models.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
