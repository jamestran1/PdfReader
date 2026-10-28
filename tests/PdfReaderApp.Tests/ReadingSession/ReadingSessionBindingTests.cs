using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>State mà XAML bind thẳng vào Phiên đọc: nguồn viewer đọc lẻ, HasDocument và thông báo đổi của chúng.</summary>
public class ReadingSessionBindingTests
{
    private static ReadingSessionFixture WithDocument(string documentId)
    {
        var f = new ReadingSessionFixture();
        f.Loader.Returns(ReadingSessionFixture.PathOf(documentId), new LoadedDocument(documentId, [], []));
        return f;
    }

    [Fact]
    public void StandaloneDocumentSource_WhenReadingSingleDocument_IsItsPath()
    {
        var f = WithDocument("D");

        f.Session.Open(ReadingSessionFixture.PathOf("D"));

        Assert.Equal(ReadingSessionFixture.PathOf("D"), f.Session.StandaloneDocumentSource);
    }

    [Fact]
    public void StandaloneDocumentSource_InWorkspaceSession_IsNullSoStandaloneViewerStaysIdle()
    {
        var f = WithDocument("D");
        f.Session.Open(ReadingSessionFixture.PathOf("D"));

        f.Session.IsWorkspaceSession = true;

        Assert.Null(f.Session.StandaloneDocumentSource);
    }

    [Fact]
    public void StandaloneDocumentSource_RaisesChanged_WhenFilePathOrModeChanges()
    {
        var f = WithDocument("D");
        var raised = new List<string?>();
        f.Session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        f.Session.Open(ReadingSessionFixture.PathOf("D"));
        f.Session.IsWorkspaceSession = true;

        Assert.Equal(2, raised.Count(name => name == nameof(ReadingSessionViewModel.StandaloneDocumentSource)));
    }

    [Fact]
    public void HasDocument_FollowsLoadedDocumentAndRaisesChanged()
    {
        var f = WithDocument("D");
        var raised = new List<string?>();
        f.Session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(f.Session.HasDocument);

        f.Session.Open(ReadingSessionFixture.PathOf("D"));

        Assert.True(f.Session.HasDocument);
        Assert.Contains(raised, name => name == nameof(ReadingSessionViewModel.HasDocument));
    }
}
