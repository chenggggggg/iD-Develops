(() => {
    const select = document.querySelector('[data-purchased-exam-select]');
    if (!select) return;
    const section = document.querySelector('[data-purchased-exam-attempts]');
    const input = section.querySelector('[data-purchased-exam-allowance]');
    function update(reset) {
        section.hidden = !select.value;
        input.disabled = !select.value;
        input.required = Boolean(select.value);
        if (select.value && (reset || !input.value)) input.value = select.selectedOptions[0].dataset.attempts || '1';
    }
    select.addEventListener('change', () => update(true));
    update(false);
})();
