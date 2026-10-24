using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class WorkflowFeedbackInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string Origin = "http://workflow.localhost";
    private const string Html = """
        <!doctype html><html><body>
        <main><h1>Workflow</h1>
            <a href="/products">Products</a>
            <a href="#section">Section</a><div id="section">Section content</div>
            <a href="/products" target="_blank">New tab</a>
            <a href="/download" download="example.txt">Download</a>
            <a href="http://outside.example.test/products">External</a>
            <form method="post" action="/save" data-submit-feedback>
                <label>Class name<input name="className" required value="Conversation" /></label>
                <button type="submit" name="operation" value="save">
                    <span data-submit-idle>Save recurring class</span>
                    <span data-submit-pending hidden>Saving recurring class...</span>
                </button>
                <button type="button" disabled>Unavailable action</button>
            </form>
        </main>
        <div data-navigation-progress data-loading-label="Loading..." data-loading-destination="Loading {0}..." role="status" hidden>
            <span data-navigation-progress-label>Loading...</span>
        </div>
        <script src="/workflow-feedback.js"></script>
        </body></html>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowNavigation_ShowsFeedbackForMouseAndKeyboardAndClearsOnBack(bool keyboard)
    {
        await InitializeFixtureAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Page.RouteAsync($"{Origin}/products", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main><h1>Products ready</h1></main>" });
        });
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Products", Exact = true });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var navigation = keyboard ? link.PressAsync("Enter") : link.ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(state.GetProperty("hidden").GetBoolean());
            Assert.Equal("Loading Products...", state.GetProperty("text").GetString());
            Assert.Equal("true", state.GetProperty("busy").GetString());
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Products ready" })).ToBeVisibleAsync();
            await navigation;
            await Page.GoBackAsync();
            await Expect(Page.Locator("[data-navigation-progress]")).ToBeHiddenAsync();
            await Expect(Page.GetByRole(AriaRole.Main)).Not.ToHaveAttributeAsync("aria-busy", "true");
        }
        finally { release.TrySetResult(); await navigation; }
    }

    [Theory]
    [InlineData("Section")]
    [InlineData("New tab")]
    [InlineData("Download")]
    public async Task NonDocumentLinks_DoNotShowPageFeedback(string name)
    {
        await InitializeFixtureAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-navigation-progress]")).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Main)).Not.ToHaveAttributeAsync("aria-busy", "true");
    }

    [Fact]
    public async Task ExternalNavigation_DoesNotShowApplicationFeedback()
    {
        await InitializeFixtureAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Context.RouteAsync("http://outside.example.test/**", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { Body = "External destination" });
        });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var navigation = Page.GetByRole(AriaRole.Link, new() { Name = "External", Exact = true }).ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(state.GetProperty("hidden").GetBoolean());
        }
        finally { release.TrySetResult(); await navigation; }
    }

    [Fact]
    public async Task CancelledLinkAndFormEvents_DoNotLeaveFeedbackOrDisableTheForm()
    {
        await InitializeFixtureAsync();
        await Page.EvaluateAsync("""
            () => {
                window.addEventListener('click', event => { if (event.target.closest('a')) event.preventDefault(); });
                window.addEventListener('submit', event => event.preventDefault());
            }
            """);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Products", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-navigation-progress]")).ToBeHiddenAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
        await Expect(Page.Locator("form")).Not.ToHaveAttributeAsync("aria-busy", "true");
    }

    [Fact]
    public async Task InvalidForm_StaysEditableWithoutShowingSavingState()
    {
        await InitializeFixtureAsync();
        await Page.GetByLabel("Class name").FillAsync("");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Class name")).ToBeFocusedAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
        await Expect(Page.Locator("form")).Not.ToHaveAttributeAsync("aria-busy", "true");
    }

    [Fact]
    public async Task PendingSave_PreventsRepeatedPostsPreservesPayloadAndRecoversAfterErrorResponse()
    {
        await InitializeFixtureAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource<IRequest>();
        var posts = 0;
        await Page.RouteAsync($"{Origin}/save", async route =>
        {
            Interlocked.Increment(ref posts);
            started.TrySetResult(route.Request);
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = Html.Replace("<h1>Workflow</h1>", "<p role='alert'>Please try again.</p>") });
        });
        var snapshot = await WorkflowFeedbackProbe.SubmissionAsync(Page);
        var submission = Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
        try
        {
            var request = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var payload = QueryHelpers.ParseQuery(request.PostData!);
            Assert.Equal("Conversation", payload["className"]);
            Assert.Equal("save", payload["operation"]);
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("true", state.GetProperty("busy").GetString());
            Assert.True(state.GetProperty("disabled").GetBoolean());
            Assert.True(state.GetProperty("pendingVisible").GetBoolean());
            Assert.True(state.GetProperty("idleHidden").GetBoolean());
            Assert.True(state.GetProperty("duplicatePrevented").GetBoolean());
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Please try again.");
            Assert.Equal(1, posts);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unavailable action", Exact = true })).ToBeDisabledAsync();
        }
        finally { release.TrySetResult(); await submission; }
    }

    [Fact]
    public async Task CancelledBeforeUnload_RestoresPendingFormState()
    {
        await InitializeFixtureAsync();
        // Test application cleanup separately from the browser's permission to
        // display a native dialog. Use real formdata and lifecycle DOM events.
        await Page.EvaluateAsync("""
            () => {
                const form = document.querySelector('form');
                form.dispatchEvent(new SubmitEvent('submit', {bubbles:true, cancelable:true}));
                new FormData(form);
            }
            """);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Saving recurring class...", Exact = true })).ToBeDisabledAsync();
        Assert.False(await Page.EvaluateAsync<bool>("""
            () => {
                window.addEventListener('beforeunload', event => event.preventDefault(), {once:true});
                return window.dispatchEvent(new Event('beforeunload', {cancelable:true}));
            }
            """));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unavailable action", Exact = true })).ToBeDisabledAsync();
        await Expect(Page.Locator("form")).Not.ToHaveAttributeAsync("aria-busy", "true");
        await Expect(Page.Locator("[data-navigation-progress]")).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Main)).Not.ToHaveAttributeAsync("aria-busy", "true");
        Assert.Equal($"{Origin}/start", Page.Url);
    }

    private async Task InitializeFixtureAsync()
    {
        await Context.RouteAsync($"{Origin}/**", route => route.FulfillAsync(new()
        {
            ContentType = route.Request.Url.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : "text/html",
            Body = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                ? File.ReadAllText(TestAssetPaths.JavaScript("workflow-feedback.js")) : Html
        }));
        await Page.GotoAsync($"{Origin}/start");
    }
}
