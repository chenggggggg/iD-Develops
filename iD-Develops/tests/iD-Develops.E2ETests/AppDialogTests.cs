namespace iD_Develops.E2ETests;

public sealed class AppDialogTests : Microsoft.Playwright.Xunit.PageTest
{
    private static string PublicBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PUBLIC_BASE_URL") ?? "http://id.localhost:5000").TrimEnd('/');

    private static string PortalBaseUrl =>
        (Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PORTAL_BASE_URL") ?? "http://portal.id.localhost:5000").TrimEnd('/');

    [Fact]
    public async Task SharedDialog_HandlesConfirmationCancellationAndTextInput()
    {
        await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.WaitForFunctionAsync("() => typeof window.appConfirm === 'function'");

        await Page.EvaluateAsync("""
            () => {
                window.__appDialogResult = "pending";
                window.appConfirm({
                    title: "Remove assignment",
                    message: "Remove this direct assignment?",
                    confirmLabel: "Remove",
                    tone: "danger"
                }).then(result => window.__appDialogResult = result);
            }
            """);

        var dialog = Page.Locator("[data-app-dialog]");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Remove assignment", Exact = true }))
            .ToBeVisibleAsync();
        var cancelButton = Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true });
        var removeButton = Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove", Exact = true });
        Assert.Equal("rgb(255, 255, 255)", await cancelButton.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.Contains("bg-red-700", await removeButton.GetAttributeAsync("class"));
        var removeBackground = await removeButton.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", removeBackground);
        Assert.NotEqual("rgb(255, 255, 255)", removeBackground);
        await cancelButton.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__appDialogResult === false");
        await Expect(dialog).ToBeHiddenAsync();

        await Page.EvaluateAsync("""
            () => {
                window.__appDialogResult = "pending";
                window.appConfirm({
                    title: "Confirm booking",
                    message: "Book this appointment?",
                    confirmLabel: "Book appointment",
                    tone: "info"
                }).then(result => window.__appDialogResult = result);
            }
            """);

        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Book appointment", Exact = true }).ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__appDialogResult === true");

        await Page.EvaluateAsync("""
            () => {
                window.__confirmedFormSubmissions = 0;
                const form = document.createElement("form");
                form.id = "confirmation-test-form";
                form.dataset.confirmTitle = "Remove assignment";
                form.dataset.confirmMessage = "Remove this direct assignment?";
                form.dataset.confirmAction = "Remove";
                form.dataset.confirmTone = "danger";
                form.innerHTML = '<button type="submit">Submit test form</button>';
                document.body.append(form);

                document.addEventListener("submit", event => {
                    if (event.target !== form || event.defaultPrevented) return;
                    event.preventDefault();
                    window.__confirmedFormSubmissions += 1;
                });

                form.requestSubmit();
            }
            """);

        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__confirmedFormSubmissions === 1");

        await Page.EvaluateAsync("""
            () => {
                window.__appDialogResult = "pending";
                window.appPrompt({
                    title: "Cancel meeting",
                    message: "Provide a reason for cancelling this meeting.",
                    inputLabel: "Cancellation reason",
                    value: "Schedule change",
                    confirmLabel: "Cancel meeting",
                    tone: "danger"
                }).then(result => window.__appDialogResult = result);
            }
            """);

        await Page.GetByLabel("Cancellation reason").FillAsync("Tutor unavailable");
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel meeting", Exact = true }).ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__appDialogResult === 'Tutor unavailable'");
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SharedNotification_RendersAccessibleErrorFeedback()
    {
        await Page.GotoAsync(
            $"{PublicBaseUrl}/en-us/",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.WaitForFunctionAsync("() => typeof window.notify === 'function'");

        await Page.EvaluateAsync("""
            () => window.notify("The requested action could not be completed.", {
                type: "error",
                title: "Action failed",
                durationMs: 0
            })
            """);

        var alert = Page.GetByRole(Microsoft.Playwright.AriaRole.Alert);
        await Expect(alert).ToContainTextAsync("Action failed");
        await Expect(alert).ToContainTextAsync("The requested action could not be completed.");
        await alert.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(alert).ToBeHiddenAsync();
    }

    [Fact]
    public async Task CourseEditor_RemoveItemUsesSharedConfirmationDialog()
    {
        var email = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_EMAIL");
        var password = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await Page.GotoAsync(
            $"{PortalBaseUrl}/login?returnUrl=%2Fcourses",
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.GetByLabel("E-mail").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"{PortalBaseUrl}/courses");

        var editLinks = Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Edit course", Exact = true });
        if (await editLinks.CountAsync() == 0)
        {
            return;
        }

        var editorHref = await editLinks.First.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(editorHref));
        await Page.GotoAsync(
            new Uri(new Uri(PortalBaseUrl), editorHref).ToString(),
            new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
        await Page.WaitForFunctionAsync("() => typeof window.appConfirm === 'function'");

        var removeButtons = Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove section", Exact = true });
        if (await removeButtons.CountAsync() == 0)
        {
            return;
        }

        await removeButtons.First.ClickAsync();
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Remove section", Exact = true }))
            .ToBeVisibleAsync();
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-app-dialog]")).ToBeHiddenAsync();
    }
}
