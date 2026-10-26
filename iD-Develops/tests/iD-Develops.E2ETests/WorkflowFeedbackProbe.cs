using System.Text.Json;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

internal static class WorkflowFeedbackProbe
{
    // Capture state after event dispatch, while the destination response is held.
    // Frame-based queries wait for the new document; immediate event listeners
    // can observe state before later application listeners have updated it.
    public static async Task<TaskCompletionSource<JsonElement>> NavigationAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.Locator("[data-navigation-progress][data-workflow-feedback-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var snapshot = new TaskCompletionSource<JsonElement>();
        await page.ExposeFunctionAsync("recordNavigationFeedback", (Action<JsonElement>)(state => { snapshot.TrySetResult(state); }));
        await page.EvaluateAsync("""
            () => window.addEventListener('click', event => {
                if (!event.target.closest('a')) return;
                setTimeout(() => {
                    const progress = document.querySelector('[data-navigation-progress]');
                    const spinner = progress.querySelector('[aria-hidden=true]');
                    window.recordNavigationFeedback({
                        hidden: progress.hidden,
                        text: progress.querySelector('[data-navigation-progress-label]').textContent.trim(),
                        busy: document.querySelector('main')?.getAttribute('aria-busy'),
                        spinnerVisible: !!spinner && spinner.getBoundingClientRect().width > 0,
                        spinnerBorder: spinner ? getComputedStyle(spinner).borderRightColor : null
                    });
                }, 0);
            })
            """);
        return snapshot;
    }

    public static async Task<TaskCompletionSource<JsonElement>> SubmissionAsync(IPage page, bool waitForReady = true)
    {
        await page.WaitForLoadStateAsync(LoadState.Load);
        if (waitForReady)
        {
            await page.Locator("[data-navigation-progress][data-workflow-feedback-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        }
        var snapshot = new TaskCompletionSource<JsonElement>();
        await page.ExposeFunctionAsync("recordSubmissionFeedback", (Action<JsonElement>)(state => { snapshot.TrySetResult(state); }));
        await page.EvaluateAsync("""
            () => window.addEventListener('submit', event => {
                const form = event.target;
                if (event.defaultPrevented || !form.hasAttribute('data-submit-feedback')) return;
                form.addEventListener('formdata', () => setTimeout(() => {
                    const duplicate = new SubmitEvent('submit', {bubbles:true, cancelable:true});
                    const duplicatePrevented = !form.dispatchEvent(duplicate);
                    window.recordSubmissionFeedback({
                        busy: form.getAttribute('aria-busy'),
                        disabled: form.querySelector('button[type=submit]').disabled,
                        pendingVisible: !form.querySelector('[data-submit-pending]').hidden,
                        idleHidden: form.querySelector('[data-submit-idle]').hidden,
                        duplicatePrevented
                    });
                }, 0), {once:true});
            })
            """);
        return snapshot;
    }
}
