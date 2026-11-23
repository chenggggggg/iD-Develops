(() => {
    "use strict";
    const fieldsIn = root => Array.from(root.querySelector('[data-open-fields]').children);
    function refresh(root) {
        const fields = fieldsIn(root);
        fields.forEach((field, index) => {
            field.disabled = false;
            field.querySelector('[data-open-field-caption]').textContent = `Answer input ${index + 1}`;
            field.querySelector('[data-open-action="move-up"]').disabled = index === 0;
            field.querySelector('[data-open-action="move-down"]').disabled = index === fields.length - 1;
            field.querySelector('[data-open-action="remove-field"]').disabled = fields.length === 1;
            const rows = field.querySelectorAll('[data-open-alternative]');
            rows.forEach((row, alternativeIndex) => {
                row.querySelector('[data-open-alternative-caption]').textContent = `Accepted answer ${alternativeIndex + 1}`;
                row.querySelector('[data-open-action="remove-alternative"]').disabled = false;
            });
            field.querySelector('[data-open-action="add-alternative"]').disabled = rows.length >= 100;
        });
        root.querySelector('[data-open-action="add-field"]').disabled = fields.length >= 20;
        root.querySelector('[data-open-count]').textContent = `${fields.length} / 20 inputs`;
        window.questionScoring?.refresh(root.closest('form'));
        root.dataset.openEditorReady = 'true';
    }
    function sync(root, announce) {
        const json = root.querySelector('[data-open-fields-json]');
        json.disabled = false;
        json.value = JSON.stringify(fieldsIn(root).map(field => ({
            Key: field.dataset.openKey,
            Label: field.querySelector('[data-open-label]').value,
            AcceptedAnswers: Array.from(field.querySelectorAll('[data-open-alternative]')).map(row => ({
                Text: row.querySelector('[data-open-accepted-text]').value,
                Score: row.querySelector('[data-open-accepted-score]').value === '' ? null : Number(row.querySelector('[data-open-accepted-score]').value)
            })).filter(answer => answer.Text.trim() !== '')
        })));
        refresh(root);
        if (announce) root.querySelector('[data-open-editor-status]').textContent = announce;
        json.dispatchEvent(new Event('input', { bubbles: true }));
    }
    document.addEventListener('input', event => {
        const root = event.target.closest?.('[data-open-answer-editor]');
        if (root && !event.target.matches('[data-open-fields-json]')) sync(root);
    }, true);
    document.addEventListener('click', event => {
        const button = event.target.closest?.('[data-open-action]');
        if (!button || button.disabled) return;
        const root = button.closest('[data-open-answer-editor]');
        if (!root) return;
        event.preventDefault();
        const action = button.dataset.openAction;
        const field = button.closest('[data-open-field]');
        let focusTarget;
        let message;
        if (action === 'add-field' && fieldsIn(root).length < 20) {
            const item = root.querySelector('[data-open-field-template]').content.firstElementChild.cloneNode(true);
            item.dataset.openKey = `input-${crypto.randomUUID()}`;
            root.querySelector('[data-open-fields]').append(item);
            focusTarget = item.querySelector('[data-open-label]');
            message = 'Answer input added.';
        } else if (action === 'remove-field' && fieldsIn(root).length > 1) {
            focusTarget = (field.previousElementSibling || field.nextElementSibling).querySelector('[data-open-label]');
            field.remove();
            message = 'Answer input removed.';
        } else if (action === 'move-up' && field.previousElementSibling) {
            field.parentElement.insertBefore(field, field.previousElementSibling);
            focusTarget = field.querySelector('[data-open-label]');
            message = 'Answer input moved up.';
        } else if (action === 'move-down' && field.nextElementSibling) {
            field.parentElement.insertBefore(field.nextElementSibling, field);
            focusTarget = field.querySelector('[data-open-label]');
            message = 'Answer input moved down.';
        } else if (action === 'add-alternative' && field.querySelectorAll('[data-open-alternative]').length < 100) {
            const item = root.querySelector('[data-open-alternative-template]').content.firstElementChild.cloneNode(true);
            field.querySelector('[data-open-alternatives]').append(item);
            focusTarget = item.querySelector('[data-open-accepted-text]');
            message = 'Accepted answer added.';
        } else if (action === 'remove-alternative') {
            button.closest('[data-open-alternative]').remove();
            focusTarget = field.querySelector('[data-open-action="add-alternative"]');
            message = 'Accepted answer removed.';
        } else return;
        sync(root, message);
        focusTarget?.focus();
    });
    function initialize() {
        document.querySelectorAll('[data-open-answer-editor]:not([data-open-editor-ready])').forEach(root => {
            if (root.closest('form')?.dataset.examAutosaveReady === 'true') refresh(root);
        });
    }
    document.addEventListener('DOMContentLoaded', initialize);
    new MutationObserver(initialize).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-exam-autosave-ready'] });
    initialize();
})();
