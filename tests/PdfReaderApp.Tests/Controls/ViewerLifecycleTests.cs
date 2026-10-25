using PdfReaderApp.Controls.Viewer;

namespace PdfReaderApp.Tests.Controls;

public class ViewerLifecycleTests
{
    [Fact]
    public void Loaded_AfterUnloaded_ResumesResources()
    {
        var lifecycle = new ViewerLifecycle();
        Assert.True(lifecycle.OnUnloaded());

        Assert.True(lifecycle.OnLoaded());
        Assert.True(lifecycle.IsAttached);
    }

    [Fact]
    public void Loaded_FirstTime_DoesNothing()
    {
        var lifecycle = new ViewerLifecycle();

        Assert.False(lifecycle.OnLoaded());
    }

    [Fact]
    public void Unloaded_Twice_ReleasesOnce()
    {
        var lifecycle = new ViewerLifecycle();
        Assert.True(lifecycle.OnUnloaded());

        Assert.False(lifecycle.OnUnloaded());
    }

    [Fact]
    public void Loaded_AfterDispose_DoesNotResume()
    {
        var lifecycle = new ViewerLifecycle();
        Assert.True(lifecycle.OnDispose());

        Assert.False(lifecycle.OnLoaded());
        Assert.False(lifecycle.OnDispose());
    }

    [Fact]
    public void Dispose_AfterUnloaded_StillReleasesOnce()
    {
        var lifecycle = new ViewerLifecycle();
        lifecycle.OnUnloaded();

        Assert.True(lifecycle.OnDispose());
    }
}
