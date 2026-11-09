using System.Text.Json;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

internal static class WorkflowFeedbackProbe
{
    public static async Task<TaskCompletionSource<JsonElement>> AppointmentSubmissionAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.Locator("[data-navigation-progress][data-workflow-feedback-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var snapshot = new TaskCompletionSource<JsonElement>();
        await page.ExposeFunctionAsync("recordAppointmentFeedback", (Action<JsonElement>)(state => snapshot.TrySetResult(state)));
        await page.EvaluateAsync("""
            () => window.addEventListener('submit', event => {
                const form = event.target;
                if (event.defaultPrevented || !form.hasAttribute('data-appointment-book-form')) return;
                form.addEventListener('formdata', () => setTimeout(() => {
                    const button = document.querySelector('[data-appointment-slots] button[aria-busy=true]');
                    const spinner = button?.querySelector('[aria-hidden=true]');
                    const duplicate = new SubmitEvent('submit', {bubbles:true, cancelable:true});
                    window.recordAppointmentFeedback({
                        busy: button?.getAttribute('aria-busy'), disabled: button?.disabled,
                        spinnerVisible: !!spinner && spinner.getBoundingClientRect().width > 0,
                        duplicatePrevented: !form.dispatchEvent(duplicate)
                    });
                }, 0), {once:true});
            })
            """);
        return snapshot;
    }

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
            () => {
            let recorded = false;
            window.addEventListener('click', event => {
                if (!event.target.closest('a')) return;
                if (recorded) return;
                recorded = true;
                setTimeout(() => {
                    const progress = document.querySelector('[data-navigation-progress]');
                    const panel = document.querySelector('[data-portal-navigation-progress]');
                    const spinner = panel?.querySelector('[data-navigation-spinner]');
                    const content = document.querySelector('[data-portal-page-content]');
                    const sidebar = document.querySelector('[data-test-sidebar], .portal-sidebar');
                    const rect = spinner?.getBoundingClientRect();
                    const mainRect = document.querySelector('main')?.getBoundingClientRect();
                    let duplicatePrevented = null;
                    if (panel && !panel.hidden) {
                        duplicatePrevented = !event.target.closest('a').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true, button:0}));
                    }
                    window.recordNavigationFeedback({
                        hidden: progress.hidden,
                        text: progress.querySelector('[data-navigation-progress-label]').textContent.trim(),
                        busy: document.querySelector('main')?.getAttribute('aria-busy'),
                        spinnerVisible: !!spinner && spinner.getBoundingClientRect().width > 0,
                        spinnerBorder: spinner ? getComputedStyle(spinner).borderRightColor : null,
                        panelHidden: panel?.hidden ?? true,
                        contentHidden: content?.hidden ?? false,
                        contentInert: content?.inert ?? false,
                        sidebarVisible: !!sidebar && sidebar.getBoundingClientRect().width > 0,
                        centered: !!rect && !!mainRect && Math.abs(rect.x + rect.width / 2 - mainRect.x - mainRect.width / 2) < 2 && Math.abs(rect.y + rect.height / 2 - mainRect.y - mainRect.height / 2) < 2,
                        animationName: spinner ? getComputedStyle(spinner).animationName : null,
                        duplicatePrevented
                    });
                }, 0);
            })
            }
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
                    const button = form.querySelector('button[type=submit][aria-busy=true]');
                    const spinners = Array.from(button?.querySelectorAll('[aria-hidden=true]') || []).filter(element => element.getBoundingClientRect().width > 0 && (element.classList.contains('animate-spin') || element.classList.contains('motion-safe:animate-spin')));
                    window.recordSubmissionFeedback({
                        busy: form.getAttribute('aria-busy'),
                        disabled: form.querySelector('button[type=submit]').disabled,
                        pendingVisible: form.querySelector('[data-submit-pending]') ? !form.querySelector('[data-submit-pending]').hidden : spinners.length > 0,
                        idleHidden: form.querySelector('[data-submit-idle]')?.hidden ?? false,
                        duplicatePrevented,
                        buttonBusy: button?.getAttribute('aria-busy'),
                        spinnerCount: spinners.length,
                        navigationHidden: document.querySelector('[data-navigation-progress]')?.hidden ?? true
                    });
                }, 0), {once:true});
            })
            """);
        return snapshot;
    }
}
