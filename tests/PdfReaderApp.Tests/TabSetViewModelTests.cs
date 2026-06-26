using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

/// <summary>
/// TDD vertical slices cho TabSetViewModel (logic Open Set).
/// Không nạp PDF thật -- chỉ test hành vi tab thuần túy.
/// </summary>
public class TabSetViewModelTests
{
    // --- Helpers ---

    private static TabSetViewModel Make() => new TabSetViewModel();

    // =========================================================
    // Hành vi 1: OpenOrActivate tài liệu mới -> nằm trong Tabs và là ActiveTab
    // =========================================================
    [Fact]
    public void OpenOrActivate_NewDoc_IsInTabsAndIsActiveTab()
    {
        var vm = Make();

        var tab = vm.OpenOrActivate("doc1", "Tài liệu 1", "/path/doc1.pdf");

        Assert.Single(vm.Tabs);
        Assert.Same(tab, vm.ActiveTab);
        Assert.True(vm.HasTabs);
    }

    // =========================================================
    // Hành vi 2: OpenOrActivate cùng DocumentId -> không tạo duplicate, tab đó là ActiveTab
    // =========================================================
    [Fact]
    public void OpenOrActivate_SameDocId_NoDuplicateAndIsActiveTab()
    {
        var vm = Make();
        var first = vm.OpenOrActivate("doc1", "Tài liệu 1", "/path/doc1.pdf");

        var second = vm.OpenOrActivate("doc1", "Tài liệu 1", "/path/doc1.pdf");

        Assert.Single(vm.Tabs);
        Assert.Same(first, second);
        Assert.Same(first, vm.ActiveTab);
    }

    // =========================================================
    // Hành vi 3: Có active tab, mở doc thứ hai -> chèn ngay sau active tab cũ, trở thành active
    // =========================================================
    [Fact]
    public void OpenOrActivate_SecondDoc_InsertedAfterActiveTab()
    {
        var vm = Make();
        var tabA = vm.OpenOrActivate("docA", "Tài liệu A", "/path/a.pdf");
        var tabC = vm.OpenOrActivate("docC", "Tài liệu C", "/path/c.pdf");
        // Kích hoạt lại tabA để tabC không còn là active
        vm.OpenOrActivate("docA", "Tài liệu A", "/path/a.pdf");

        // Mở tabB khi đang ở tabA -> phải chèn ngay sau vị trí tabA
        var tabB = vm.OpenOrActivate("docB", "Tài liệu B", "/path/b.pdf");

        // Thứ tự: A, B, C
        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal(tabA, vm.Tabs[0]);
        Assert.Equal(tabB, vm.Tabs[1]);
        Assert.Equal(tabC, vm.Tabs[2]);
        Assert.Same(tabB, vm.ActiveTab);
    }

    // =========================================================
    // Hành vi 4: Đóng tab không active -> bị xóa, ActiveTab không đổi
    // =========================================================
    [Fact]
    public void Close_NonActiveTab_RemovedAndActivetabUnchanged()
    {
        var vm = Make();
        var tabA = vm.OpenOrActivate("docA", "Tài liệu A", "/path/a.pdf");
        var tabB = vm.OpenOrActivate("docB", "Tài liệu B", "/path/b.pdf");
        // tabB là active; đóng tabA (không active)

        vm.Close(tabA);

        Assert.Single(vm.Tabs);
        Assert.DoesNotContain(tabA, vm.Tabs);
        Assert.Same(tabB, vm.ActiveTab);
    }

    // =========================================================
    // Hành vi 5: Đóng active tab -> ActiveTab là tab MRU gần nhất (không phải hàng xóm bên trái)
    // Kịch bản: mở A, B, C theo thứ tự; kích hoạt A (MRU: A > C > B); đóng A
    //           -> ActiveTab phải là C (MRU), không phải B (hàng xóm trái trong Strip).
    // =========================================================
    [Fact]
    public void Close_ActiveTab_FallsBackToMruNotLeftNeighbor()
    {
        var vm = Make();
        var tabA = vm.OpenOrActivate("docA", "A", "/a.pdf"); // MRU: [A]
        var tabB = vm.OpenOrActivate("docB", "B", "/b.pdf"); // MRU: [B, A]
        var tabC = vm.OpenOrActivate("docC", "C", "/c.pdf"); // MRU: [C, B, A]
        // Kích hoạt A -> MRU: [A, C, B]; Tabs vẫn A B C
        vm.OpenOrActivate("docA", "A", "/a.pdf");

        // A đang active, hàng xóm trái là null (A ở vị trí 0), hàng xóm phải là B.
        // MRU tiếp theo sau A là C. Test phân biệt MRU vs hàng xóm.
        vm.Close(tabA);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.DoesNotContain(tabA, vm.Tabs);
        // Phải là C (MRU), không phải B (hàng xóm phải trong Strip)
        Assert.Same(tabC, vm.ActiveTab);
    }

    // =========================================================
    // Hành vi 6: Đóng tab cuối cùng -> ActiveTab là null, HasTabs là false
    // =========================================================
    [Fact]
    public void Close_LastTab_ActiveTabNullAndHasTabsFalse()
    {
        var vm = Make();
        var tab = vm.OpenOrActivate("docA", "A", "/a.pdf");

        vm.Close(tab);

        Assert.Null(vm.ActiveTab);
        Assert.False(vm.HasTabs);
        Assert.Empty(vm.Tabs);
    }

    // =========================================================
    // Hành vi 7: Per-tab view-state sống trên OpenTab, sống sót qua chuyển tab
    // =========================================================
    [Fact]
    public void PerTabViewState_SurvivesTabSwitch()
    {
        var vm = Make();
        var tabA = vm.OpenOrActivate("docA", "A", "/a.pdf");
        var tabB = vm.OpenOrActivate("docB", "B", "/b.pdf");

        // Thiết lập view-state cho tabA
        tabA.Page = 7;
        tabA.Zoom = 1.5;

        // Chuyển sang tabB, rồi quay lại tabA
        vm.OpenOrActivate("docB", "B", "/b.pdf");
        vm.OpenOrActivate("docA", "A", "/a.pdf");

        // View-state của tabA phải được giữ nguyên
        Assert.Same(tabA, vm.ActiveTab);
        Assert.Equal(7, tabA.Page);
        Assert.Equal(1.5, tabA.Zoom);
    }

    // =========================================================
    // Cross-doc jump: initialPage được đặt TRƯỚC khi kích hoạt (để viewer mở thẳng tại trang đích).
    // =========================================================
    [Fact]
    public void OpenOrActivate_WithInitialPage_SetsPageOnNewTab()
    {
        var vm = Make();

        var tab = vm.OpenOrActivate("docA", "A", "/a.pdf", initialPage: 15);

        Assert.Equal(15, tab.Page);
        Assert.Same(tab, vm.ActiveTab);
    }

    [Fact]
    public void OpenOrActivate_WithInitialPage_UpdatesExistingTabPage()
    {
        var vm = Make();
        var tabA = vm.OpenOrActivate("docA", "A", "/a.pdf");
        vm.OpenOrActivate("docB", "B", "/b.pdf"); // tabB active

        // Cross-doc jump quay lại docA tại trang 9
        var again = vm.OpenOrActivate("docA", "A", "/a.pdf", initialPage: 9);

        Assert.Same(tabA, again);
        Assert.Equal(9, tabA.Page);
        Assert.Same(tabA, vm.ActiveTab);
    }

    // =========================================================
    // S2: RestoreTabs khôi phục tất cả tab theo thứ tự, kích hoạt đúng tab được chỉ định,
    //     chỉ phát đúng một ActiveTabChanged (-> đúng một HydrateTab), và giữ view-state.
    // =========================================================
    [Fact]
    public void RestoreTabs_AddsAllInOrder_ActivatesDesignated_PreservesViewState()
    {
        var tabSet = new TabSetViewModel();
        int activationCount = 0;
        tabSet.ActiveTabChanged += _ => activationCount++;

        var tabA = new OpenTab("docA", "A", "/a.pdf") { Page = 3, Zoom = 1.5 };
        var tabB = new OpenTab("docB", "B", "/b.pdf") { Page = 7, Zoom = 2.0 };
        tabSet.RestoreTabs(new[] { tabA, tabB }, activeDocumentId: "docB");

        Assert.Equal(new[] { "docA", "docB" }, tabSet.Tabs.Select(t => t.DocumentId));
        Assert.Same(tabB, tabSet.ActiveTab);
        Assert.Equal(1, activationCount);
        Assert.Equal(7, tabSet.ActiveTab!.Page);
    }
}
