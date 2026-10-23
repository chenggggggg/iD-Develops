using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ContactSubmissionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Theory]
    [InlineData("en-us", "Submit", "Message sent!", "We have received your message", "Hello")]
    [InlineData("nl-nl", "Versturen", "Bericht verzonden!", "We hebben je bericht ontvangen", "Hallo")]
    public async Task ValidSubmission_WritesTeamNotificationAndLocalizedReceipt(
        string culture, string button, string feedback, string subject, string greeting)
    {
        var directory = FileMailDirectory();
        var marker = $"E2E-{Guid.NewGuid():N}";
        var name = $"{marker} <b>Name</b>";
        var recipient = $"contact-{Guid.NewGuid():N}@example.test";
        try
        {
            await FillContactAsync(culture, name, recipient);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = button, Exact = true })).ToBeVisibleAsync();
            var response = await Page.RunAndWaitForResponseAsync(
                () => Page.GetByRole(AriaRole.Button, new() { Name = button, Exact = true }).ClickAsync(),
                response => response.Request.Method == "POST");

            Assert.True(response.Ok, $"Contact submission returned HTTP {response.Status}.");
            await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync(feedback);
            var mails = MatchingMails(directory, marker);
            Assert.Equal(2, mails.Count);
            var receipt = Assert.Single(mails, mail => mail.Body.StartsWith($"To: {recipient}", StringComparison.Ordinal));
            var notification = Assert.Single(mails, mail => mail.Path != receipt.Path);
            Assert.Contains("Subject: Contact Form | E2E contact inquiry", notification.Body, StringComparison.Ordinal);
            Assert.Contains("Private submission text for our team.", notification.Body, StringComparison.Ordinal);
            Assert.Contains($"Subject: {subject}", receipt.Body, StringComparison.Ordinal);
            Assert.Contains($"{greeting} {marker} &lt;b&gt;Name&lt;/b&gt;", receipt.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("<b>Name</b>", receipt.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Private submission text for our team.", receipt.Body, StringComparison.Ordinal);
            Assert.Contains($"lang=\"{culture}\"", receipt.Body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var mail in MatchingMails(directory, marker))
                File.Delete(mail.Path);
        }
    }

    [Fact]
    public async Task InvalidSubmission_ShowsValidationWithoutSendingEmail()
    {
        var directory = FileMailDirectory();
        var marker = $"E2E-{Guid.NewGuid():N}";
        try
        {
            await FillContactAsync("en-us", marker, $"contact-{Guid.NewGuid():N}@example.test");
            await Page.GetByLabel("Message", new() { Exact = true }).FillAsync(new string('x', 501));
            var response = await Page.RunAndWaitForResponseAsync(
                () => Page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync(),
                response => response.Request.Method == "POST");

            Assert.True(response.Ok);
            await Expect(Page.GetByText("Message cannot exceed 500 characters.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
            Assert.Empty(MatchingMails(directory, marker));
        }
        finally
        {
            foreach (var mail in MatchingMails(directory, marker))
                File.Delete(mail.Path);
        }
    }

    private async Task FillContactAsync(string culture, string name, string email)
    {
        var response = await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/contact");
        Assert.NotNull(response);
        Assert.True(response!.Ok);
        await Expect(Page.Locator(".cf-turnstile")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = culture == "nl-nl" ? "Naam" : "Name", Exact = true }).FillAsync(name);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = culture == "nl-nl" ? "E-mail" : "Email", Exact = true }).FillAsync(email);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = culture == "nl-nl" ? "Onderwerp" : "Subject", Exact = true }).FillAsync("E2E contact inquiry");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = culture == "nl-nl" ? "Bericht" : "Message", Exact = true }).FillAsync("Private submission text for our team.");
    }

    private static string FileMailDirectory()
    {
        var host = new Uri(LocalTestApplication.PublicBaseUrl).Host;
        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
            !(System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address)))
            throw new InvalidOperationException("Contact submission tests require a local application with Mail:Provider=File and Turnstile disabled.");

        var directory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            throw new InvalidOperationException("Set IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY to the running File mail provider's absolute pickup directory; these tests must not send real email.");
        return directory;
    }

    private static List<(string Path, string Body)> MatchingMails(string directory, string marker)
        => !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.html")
            .Select(path => (Path: path, Body: File.ReadAllText(path)))
            .Where(mail => mail.Body.Contains(marker, StringComparison.Ordinal)).ToList();
}
