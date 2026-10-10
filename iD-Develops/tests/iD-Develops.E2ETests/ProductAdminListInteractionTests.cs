namespace iD_Develops.E2ETests;

public sealed class ProductAdminListInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task ProductTabs_SwitchTablesUpdateTheUrlAndSupportKeyboardNavigation()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <div role="tablist" data-product-list-tabs>
                    <button type="button" role="tab" aria-selected="true" aria-controls="product-panel-all" tabindex="0" data-product-list-tab data-product-list-target="product-panel-all" data-product-list-key="all">All</button>
                    <button type="button" role="tab" aria-selected="false" aria-controls="product-panel-booking" tabindex="-1" data-product-list-tab data-product-list-target="product-panel-booking" data-product-list-key="booking">Booking</button>
                    <button type="button" role="tab" aria-selected="false" aria-controls="product-panel-archived" tabindex="-1" data-product-list-tab data-product-list-target="product-panel-archived" data-product-list-key="archived">Archived</button>
                </div>
                <section id="product-panel-all" role="tabpanel" data-product-list-panel>All products</section>
                <section id="product-panel-booking" role="tabpanel" data-product-list-panel hidden>Booking products</section>
                <section id="product-panel-archived" role="tabpanel" data-product-list-panel hidden>Archived products</section>
            </body>
            </html>
            """);

        var scriptPath = TestAssetPaths.JavaScript("product-admin-list.js");
        await Page.AddScriptTagAsync(new() { Path = scriptPath });

        var archivedTab = Page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Archived" });
        await archivedTab.ClickAsync();

        await Expect(archivedTab).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.GetByText("Archived products", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("All products", new() { Exact = true })).ToBeHiddenAsync();
        Assert.EndsWith("?tab=archived", Page.Url, StringComparison.Ordinal);

        await archivedTab.PressAsync("ArrowLeft");

        var bookingTab = Page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Booking" });
        await Expect(bookingTab).ToBeFocusedAsync();
        await Expect(bookingTab).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.GetByText("Booking products", new() { Exact = true })).ToBeVisibleAsync();
        Assert.EndsWith("?tab=booking", Page.Url, StringComparison.Ordinal);
    }
}
