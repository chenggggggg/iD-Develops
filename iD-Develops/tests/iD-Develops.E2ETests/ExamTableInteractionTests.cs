namespace iD_Develops.E2ETests;

public sealed class ExamTableInteractionTests : Microsoft.Playwright.Xunit.PageTest
{
    [Fact]
    public async Task MoreActionsPopover_EscapesTableOverflowAndStaysAboveRows()
    {
        await Page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <body>
                <div id="table-clip" style="height:40px;overflow:hidden">
                    <button type="button"
                            id="exam-actions-trigger"
                            popovertarget="exam-actions-1"
                            data-exam-actions-trigger="exam-actions-1">More</button>
                    <div id="exam-actions-1"
                         popover="auto"
                         data-exam-actions-popover
                         style="width:176px;height:120px">
                        <button type="button">Delete</button>
                    </div>
                </div>
            </body>
            </html>
            """);

        var scriptPath = TestAssetPaths.JavaScript("exams-list.js");
        await Page.AddScriptTagAsync(new() { Path = scriptPath });
        await Page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");

        var popover = Page.Locator("#exam-actions-1");
        await Expect(popover).ToBeHiddenAsync();

        await Page.Locator("#exam-actions-trigger").ClickAsync();
        await Expect(popover).ToBeVisibleAsync();
        Assert.True(await popover.EvaluateAsync<bool>("element => element.matches(':popover-open')"));

        var hitTarget = await Page.EvaluateAsync<string>(
            """
            () => {
                const menu = document.getElementById('exam-actions-1');
                const bounds = menu.getBoundingClientRect();
                const blocker = document.createElement('div');
                blocker.style.position = 'fixed';
                blocker.style.inset = `${bounds.top}px auto auto ${bounds.left}px`;
                blocker.style.width = `${bounds.width}px`;
                blocker.style.height = `${bounds.height}px`;
                blocker.style.zIndex = '2147483647';
                document.body.appendChild(blocker);
                return document.elementFromPoint(bounds.left + 8, bounds.top + 8)
                    ?.closest('#exam-actions-1')?.id || '';
            }
            """);

        Assert.Equal("exam-actions-1", hitTarget);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(popover).ToBeHiddenAsync();
        Assert.False(await popover.EvaluateAsync<bool>("element => element.matches(':popover-open')"));
    }
}
