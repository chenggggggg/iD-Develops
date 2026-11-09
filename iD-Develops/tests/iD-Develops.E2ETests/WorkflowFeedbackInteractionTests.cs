using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class WorkflowFeedbackInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    private const string Origin = "http://workflow.localhost";
    private const string Html = """
        <!doctype html><html><head><link rel="stylesheet" href="/tailwind.css"></head><body class="flex">
        <aside data-test-sidebar class="p-4">
            <a href="/products">Products</a>
            <a href="#section">Section</a><div id="section">Section content</div>
            <a href="/products" target="_blank">New tab</a>
            <a href="/download" download="example.txt">Download</a>
            <a href="http://outside.example.test/products">External</a>
            <a href="http://public.workflow.localhost/products">Public website</a>
        </aside>
        <main class="relative min-h-screen min-w-0 flex-1"><section data-portal-page-content class="p-4"><h1>Workflow</h1>
            <form method="post" action="/save" data-submit-feedback>
                <label>Class name<input name="className" required value="Conversation" /></label>
                <button type="submit" name="operation" value="save">
                    <span data-submit-idle>Save recurring class</span>
                    <span data-submit-pending hidden class="inline-flex items-center gap-2">Saving recurring class...</span>
                </button>
                <button type="button" disabled>Unavailable action</button>
            </form>
        </section><div data-portal-navigation-progress hidden class="absolute inset-0 grid place-content-center" aria-hidden="true">
            <span data-navigation-spinner class="size-6 rounded-full border-2 border-current border-r-transparent motion-safe:animate-spin"></span>
        </div></main>
        <div data-navigation-progress data-portal-url="http://workflow.localhost" data-public-url="http://public.workflow.localhost" data-loading-label="Loading..." data-loading-destination="Loading {0}..." role="status" class="sr-only" hidden>
            <span data-navigation-progress-label>Loading...</span>
        </div>
        <script src="/workflow-feedback.js"></script>
        </body></html>
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task SlowNavigation_ShowsFeedbackForMouseAndKeyboardAndClearsOnBack(bool keyboard, bool delayedScript, bool reducedMotion)
    {
        await InitializeFixtureAsync(loadWorkflow: !delayedScript);
        if (reducedMotion) await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Page.RouteAsync($"{Origin}/products", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main><h1>Products ready</h1></main>" });
        });
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Products", Exact = true });
        var observation = WorkflowFeedbackProbe.NavigationAsync(Page);
        if (delayedScript)
        {
            await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("workflow-feedback.js") });
        }
        var snapshot = await observation;
        var navigation = keyboard ? link.PressAsync("Enter") : link.ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(state.GetProperty("hidden").GetBoolean());
            Assert.Equal("Loading Products...", state.GetProperty("text").GetString());
            Assert.Equal("true", state.GetProperty("busy").GetString());
            Assert.True(state.GetProperty("contentHidden").GetBoolean());
            Assert.True(state.GetProperty("contentInert").GetBoolean());
            Assert.True(state.GetProperty("sidebarVisible").GetBoolean());
            Assert.True(state.GetProperty("spinnerVisible").GetBoolean());
            Assert.True(state.GetProperty("centered").GetBoolean(), state.ToString());
            Assert.True(state.GetProperty("duplicatePrevented").GetBoolean());
            if (reducedMotion) Assert.Equal("none", state.GetProperty("animationName").GetString());
            release.TrySetResult();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Products ready" })).ToBeVisibleAsync();
            await navigation;
            await Page.GoBackAsync();
            await Expect(Page.Locator("[data-navigation-progress]")).ToBeHiddenAsync();
            await Expect(Page.GetByRole(AriaRole.Main)).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Expect(Page.Locator("[data-portal-page-content]")).ToBeVisibleAsync();
            Assert.False(await Page.Locator("[data-portal-page-content]").EvaluateAsync<bool>("element => element.inert"));
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

    [Theory]
    [InlineData("External", "outside.example.test")]
    [InlineData("Public website", "public.workflow.localhost")]
    public async Task ExternalNavigation_DoesNotShowApplicationFeedback(string linkName, string host)
    {
        await InitializeFixtureAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await Context.RouteAsync($"http://{host}/**", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { Body = "External destination" });
        });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var navigation = Page.GetByRole(AriaRole.Link, new() { Name = linkName, Exact = true }).ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(state.GetProperty("hidden").GetBoolean());
        }
        finally { release.TrySetResult(); await navigation; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicNavigationKeepsContentVisibleWithoutApplicationProgress(bool keyboard)
    {
        await InitializeFixtureAsync(portal: false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync($"{Origin}/products", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main><h1>Public destination</h1></main>" });
        });
        var snapshot = await WorkflowFeedbackProbe.NavigationAsync(Page);
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Products", Exact = true });
        var click = keyboard ? link.PressAsync("Enter") : link.ClickAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(state.GetProperty("hidden").GetBoolean());
            Assert.False(state.GetProperty("spinnerVisible").GetBoolean());
            Assert.False(state.GetProperty("contentHidden").GetBoolean());
            Assert.NotEqual("true", state.GetProperty("busy").GetString());
        }
        finally { release.TrySetResult(); await click; }
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Public destination" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlainPublicSubmitButtonShowsOneSpinnerPreservesPayloadAndRecoversFromFailure()
    {
        await InitializeFixtureAsync(portal: false, plainButton: true);
        var responseHtml = (await Page.ContentAsync()).Replace("<h1>Workflow</h1>", "<p role='alert'>Please try again.</p>");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        await Page.RouteAsync($"{Origin}/save", async route =>
        {
            Interlocked.Increment(ref posts);
            started.TrySetResult(route.Request);
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "text/html", Body = responseHtml });
        });
        var snapshot = await WorkflowFeedbackProbe.SubmissionAsync(Page);
        var submit = Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true }).ClickAsync();
        try
        {
            var request = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var payload = QueryHelpers.ParseQuery(request.PostData!);
            Assert.Equal("Conversation", payload["className"]);
            Assert.Equal("save", payload["operation"]);
            var state = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(state.GetProperty("disabled").GetBoolean());
            Assert.Equal("true", state.GetProperty("buttonBusy").GetString());
            Assert.Equal(1, state.GetProperty("spinnerCount").GetInt32());
            Assert.True(state.GetProperty("navigationHidden").GetBoolean());
            Assert.True(state.GetProperty("duplicatePrevented").GetBoolean());
        }
        finally { release.TrySetResult(); await submit; }
        Assert.Equal(1, posts);
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Please try again.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save recurring class", Exact = true })).ToBeEnabledAsync();
        await Expect(Page.Locator("[data-submit-spinner]")).ToHaveCountAsync(0);
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
        await Expect(Page.Locator("[data-portal-page-content]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-portal-navigation-progress]")).ToBeHiddenAsync();
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PendingSave_PreventsRepeatedPostsPreservesPayloadAndRecoversAfterErrorResponse(bool probeBeforeFeedback, bool existingSpinner)
    {
        await InitializeFixtureAsync(loadWorkflow: !probeBeforeFeedback);
        if (existingSpinner) await Page.Locator("[data-submit-pending]").EvaluateAsync("element => element.insertAdjacentHTML('afterbegin', '<span aria-hidden=\"true\" class=\"size-4 rounded-full border-2 border-current border-r-transparent motion-safe:animate-spin\"></span>')");
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
        var snapshot = await WorkflowFeedbackProbe.SubmissionAsync(Page, waitForReady: !probeBeforeFeedback);
        if (probeBeforeFeedback)
        {
            await Page.AddScriptTagAsync(new() { Path = TestAssetPaths.JavaScript("workflow-feedback.js") });
        }
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
            Assert.Equal("true", state.GetProperty("buttonBusy").GetString());
            Assert.Equal(1, state.GetProperty("spinnerCount").GetInt32());
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

    private async Task InitializeFixtureAsync(bool loadWorkflow = true, bool portal = true, bool plainButton = false)
    {
        var fixtureHtml = loadWorkflow ? Html : Html.Replace("<script src=\"/workflow-feedback.js\"></script>", "");
        if (!portal) fixtureHtml = fixtureHtml.Replace("data-portal-page-content", "data-public-page-content").Replace("data-portal-navigation-progress", "data-unused-navigation-progress");
        if (plainButton) fixtureHtml = fixtureHtml.Replace("<span data-submit-idle>Save recurring class</span>", "Save recurring class")
            .Replace("<span data-submit-pending hidden class=\"inline-flex items-center gap-2\">Saving recurring class...</span>", "");
        await Context.RouteAsync($"{Origin}/**", route => route.FulfillAsync(new()
        {
            ContentType = route.Request.Url.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : route.Request.Url.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : "text/html",
            Body = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                ? File.ReadAllText(TestAssetPaths.JavaScript("workflow-feedback.js")) : route.Request.Url.EndsWith(".css", StringComparison.Ordinal)
                    ? File.ReadAllText(TestAssetPaths.Css("tailwind.generated.css")) : fixtureHtml
        }));
        await Page.GotoAsync($"{Origin}/start");
    }
}
