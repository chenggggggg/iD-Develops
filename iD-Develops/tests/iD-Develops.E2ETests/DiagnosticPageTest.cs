using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public abstract class DiagnosticPageTest : Microsoft.Playwright.Xunit.PageTest
{
    private readonly ConcurrentQueue<object> _events = new();
    private readonly string _artifactId = Guid.NewGuid().ToString("N");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        Context.Request += (_, request) =>
        {
            if (request.IsNavigationRequest)
                Record("navigation", request.Url, request.Method);
        };
        Context.RequestFailed += (_, request) => Record("request-failed", request.Url, request.Failure);
        Context.Response += (_, response) =>
        {
            if (response.Status >= 400 || response.Request.IsNavigationRequest)
                Record("response", response.Url, $"HTTP {response.Status}", response.Headers.GetValueOrDefault("x-request-id"));
        };
        void ObservePage(IPage page)
        {
            page.PageError += (_, error) => Record("page-error", page.Url, error);
            page.Console += (_, message) =>
            {
                if (message.Type == "error") Record("console-error", page.Url, message.Text);
            };
        }
        foreach (var page in Context.Pages) ObservePage(page);
        Context.Page += (_, page) => ObservePage(page);
    }

    public override async Task DisposeAsync()
    {
        try
        {
            // Playwright's xUnit base provides best-effort failure detection.
            // Keep its diagnostics on failure without adding retries to the test.
            if (!TestOk)
            {
                var directory = Path.Combine(AppContext.BaseDirectory, "test-results", $"{GetType().Name}-{_artifactId}");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "browser-events.json"), JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true }));
                await Context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") });
                Console.WriteLine($"Browser diagnostics: {directory}");
            }
            else
            {
                await Context.Tracing.StopAsync();
            }
        }
        catch (Exception exception)
        {
            // An artifact failure must not hide the original test failure.
            Console.Error.WriteLine($"Could not save browser diagnostics: {exception.GetType().Name}");
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private void Record(string kind, string url, string? message, string? requestId = null)
        => _events.Enqueue(new { time = DateTimeOffset.UtcNow, kind, url = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : "unknown", message, requestId });
}
