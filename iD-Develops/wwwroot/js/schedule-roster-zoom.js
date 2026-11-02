(() => {
    const form = document.querySelector('[data-roster-form]');
    const panel = form?.querySelector('[data-zoom-readiness]');
    if (!panel) return;
    const teacher = form.querySelector('[data-roster-teacher]');
    const delivery = form.querySelector('[data-roster-field="DeliveryType"]');
    const message = panel.querySelector('[data-zoom-readiness-message]');
    const spinner = panel.querySelector('[data-zoom-readiness-spinner]');
    let controller;
    let key;

    async function update() {
        const nextKey = `${teacher?.value}:${delivery?.value}`;
        if (key === nextKey) return;
        key = nextKey;
        controller?.abort();
        panel.hidden = delivery?.value !== 'Zoom';
        spinner.hidden = true;
        panel.removeAttribute('aria-busy');
        if (panel.hidden) return;
        if (!teacher?.value) {
            message.textContent = 'Select a teacher to check their Zoom connection.';
            return;
        }
        controller = new AbortController();
        const requestController = controller;
        const url = new URL(panel.dataset.readinessUrl, location.origin);
        url.searchParams.set('teacherUserId', teacher.value);
        message.textContent = 'Checking Zoom setup...';
        spinner.hidden = false;
        panel.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { credentials: 'same-origin', signal: requestController.signal });
            if (!response.ok) throw new Error('Readiness check failed');
            const status = await response.json();
            if (typeof status.message !== 'string') throw new Error('Invalid readiness response');
            if (requestController.signal.aborted) return;
            message.textContent = status.message;
            const pending = status.state !== 'Ready';
            ['border-amber-200', 'bg-amber-50', 'text-amber-950'].forEach(token => panel.classList.toggle(token, pending));
            ['border-slate-200', 'bg-slate-50', 'text-slate-700'].forEach(token => panel.classList.toggle(token, !pending));
        } catch (error) {
            if (error.name !== 'AbortError') message.textContent = 'Zoom setup could not be checked. The connection will be checked when you save; the schedule can remain pending.';
        } finally {
            if (!requestController.signal.aborted) {
                spinner.hidden = true;
                panel.removeAttribute('aria-busy');
            }
        }
    }

    form.addEventListener('change', () => queueMicrotask(update));
    document.addEventListener('click', event => {
        if (event.target.closest('[data-roster-edit], [data-roster-new], [data-roster-reset]')) queueMicrotask(update);
    });
    update();
})();
