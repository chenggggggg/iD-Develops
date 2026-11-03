(() => {
    const root = document.querySelector('[data-audit-log]');
    if (!root) return;
    const form = root.querySelector('[data-audit-filters]');
    const allDates = form.querySelector('[data-audit-all-dates]');
    const dates = form.querySelectorAll('[data-audit-date]');
    allDates.addEventListener('change', () => dates.forEach(input => { input.disabled = allDates.checked; }));
    form.addEventListener('submit', () => {
        form.querySelector('button[type=submit]').disabled = true;
        form.querySelector('[data-audit-filter-spinner]').hidden = false;
    });
    window.addEventListener('pageshow', () => {
        form.querySelector('button[type=submit]').disabled = false;
        form.querySelector('[data-audit-filter-spinner]').hidden = true;
    });

    const pages = root.querySelector('[data-audit-pages]');
    const status = root.querySelector('[data-audit-status]');
    let loading = false;
    root.addEventListener('click', async event => {
        const link = event.target instanceof Element ? event.target.closest('[data-audit-load-more]') : null;
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        if (loading) return;
        loading = true;
        status.hidden = true;
        link.setAttribute('aria-disabled', 'true');
        link.setAttribute('aria-busy', 'true');
        const label = link.querySelector('[data-audit-more-label]');
        const spinner = link.querySelector('[data-audit-more-spinner]');
        label.textContent = 'Loading…';
        spinner.hidden = false;
        pages.setAttribute('aria-busy', 'true');
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 20000);
        try {
            const url = new URL(link.href);
            url.searchParams.set('handler', 'More');
            const response = await fetch(url, { credentials: 'same-origin', signal: controller.signal });
            if (!response.ok || response.redirected) throw new Error('Audit log request failed');
            const parsed = new DOMParser().parseFromString(await response.text(), 'text/html');
            const batch = parsed.querySelector('[data-audit-page]');
            if (!batch) throw new Error('Audit log response is invalid');
            const firstNewRow = batch.querySelector('[data-audit-event] > summary');
            pages.querySelector('[data-audit-pagination]')?.remove();
            pages.append(batch);
            const count = pages.querySelectorAll('[data-audit-event]').length;
            root.querySelector('[data-audit-count]').textContent = 'Showing ' + count + ' of ' + batch.dataset.totalCount + ' matching events.';
            firstNewRow?.focus({ preventScroll: true });
        } catch {
            status.textContent = 'Could not load more events. Please try again.';
            status.hidden = false;
        } finally {
            clearTimeout(timeout);
            loading = false;
            pages.removeAttribute('aria-busy');
            link.removeAttribute('aria-disabled');
            link.removeAttribute('aria-busy');
            label.textContent = 'Load more';
            spinner.hidden = true;
        }
    });
})();
