using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Tab Bản soạn trong TabSetViewModel (ADR 0009): chung một tập tab với Tab Document, đứng ngoài hydrate/evict.</summary>
public class TabSetCompositionTests
{
    private static Composition CompositionOf(string id) => new(id, "ws", $"Bản {id}", CompositionKind.Summary, "x", "m", [], 1);

    [Fact]
    public void OpenOrActivateComposition_InsertsRightOfActiveTab_AndReactivatesInsteadOfDuplicating()
    {
        var tabSet = new TabSetViewModel();
        var tabA = tabSet.OpenOrActivate("docA", "A", "/a.pdf");
        tabSet.OpenOrActivate("docB", "B", "/b.pdf");
        tabSet.ActivateTab(tabA);

        var composition = tabSet.OpenOrActivateComposition(CompositionOf("c1"));
        tabSet.ActivateTab(tabA);
        var again = tabSet.OpenOrActivateComposition(CompositionOf("c1"));

        Assert.Same(composition, again);
        Assert.Equal(["docA", "c1", "docB"], [.. tabSet.Tabs.Select(t => t.ItemId)]);
        Assert.Same(composition, tabSet.ActiveTab);
    }

    [Fact]
    public void OpenOrActivateComposition_AfterAnotherComposition_InsertsRightOfThatTabEvenWhenItIsNotActive()
    {
        var tabSet = new TabSetViewModel();
        tabSet.OpenOrActivate("docA", "A", "/a.pdf");
        tabSet.OpenOrActivateComposition(CompositionOf("x"));
        tabSet.OpenOrActivate("docB", "B", "/b.pdf");

        var regenerated = tabSet.OpenOrActivateComposition(CompositionOf("x2"), afterCompositionId: "x");

        Assert.Equal(["docA", "x", "x2", "docB"], [.. tabSet.Tabs.Select(t => t.ItemId)]);
        Assert.Same(regenerated, tabSet.ActiveTab);
    }

    [Fact]
    public void OpenOrActivate_DocumentWithSameIdAsComposition_IsADifferentTab()
    {
        var tabSet = new TabSetViewModel();
        tabSet.OpenOrActivateComposition(CompositionOf("same"));

        var document = tabSet.OpenOrActivate("same", "Doc", "/d.pdf");

        Assert.Equal(2, tabSet.Tabs.Count);
        Assert.Same(document, tabSet.ActiveTab);
    }

    [Fact]
    public void CompositionTab_DoesNotCountTowardsTheWarmLimitOfDocumentTabs()
    {
        var tabSet = new TabSetViewModel(maxWarmInactiveTabs: 1);
        var tabA = tabSet.OpenOrActivate("docA", "A", "/a.pdf");
        tabSet.OpenOrActivate("docB", "B", "/b.pdf");

        tabSet.OpenOrActivateComposition(CompositionOf("c1"));   // MRU: c1, B, A

        Assert.True(tabA.IsHydrated);
    }

    [Fact]
    public void ClosingActiveCompositionTab_ActivatesMostRecentlyUsedTab()
    {
        var tabSet = new TabSetViewModel();
        var tabA = tabSet.OpenOrActivate("docA", "A", "/a.pdf");
        var composition = tabSet.OpenOrActivateComposition(CompositionOf("c1"));

        tabSet.Close(composition);

        Assert.Same(tabA, tabSet.ActiveTab);
    }
}
