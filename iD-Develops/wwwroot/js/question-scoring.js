(() => {
    "use strict";
    const text = (element, value) => {
        if (element && element.textContent !== value) element.textContent = value;
    };
    function refresh(form) {
        const panel = form?.querySelector('[data-question-scoring]');
        if (!panel) return true;
        const input = panel.querySelector('[data-question-maximum]');
        const ready = form.dataset.examAutosaveReady === 'true';
        input.readOnly = !ready;
        input.setAttribute('aria-busy', String(!ready));
        const maximum = Number(input.value);
        const validMaximum = input.value.trim() !== '' && Number.isFinite(maximum) && maximum > 0 && !input.validity.badInput;
        const error = panel.querySelector('[data-question-points-error]');
        input.setAttribute('aria-invalid', String(!validMaximum));
        error.hidden = validMaximum;
        text(error, validMaximum ? '' : 'Enter a maximum greater than zero.');
        const amount = validMaximum ? String(maximum) : '—';
        const unit = maximum === 1 ? 'point' : 'points';
        text(panel.querySelector('[data-question-award-summary]'), panel.dataset.scoringKind === 'Open'
            ? `Up to ${amount} ${unit} for this whole question.`
            : `Correct: ${amount} ${unit}. Incorrect or unanswered: 0.`);
        let valid = validMaximum;
        form.querySelectorAll('[data-open-field]').forEach((field, fieldIndex) => {
            field.querySelectorAll('[data-open-alternative]').forEach((row, index) => {
                const award = row.querySelector('[data-open-accepted-score]');
                const hint = row.querySelector('[data-open-award-limit]');
                const message = row.querySelector('[data-open-award-error]');
                const id = `open-award-${fieldIndex}-${index}`;
                hint.id = `${id}-limit`;
                message.id = `${id}-error`;
                award.setAttribute('aria-describedby', `${hint.id} ${message.id}`);
                award.placeholder = validMaximum ? `Full (${amount})` : 'Full';
                if (validMaximum) award.max = String(maximum);
                else award.removeAttribute('max');
                text(hint, validMaximum ? `0–${amount} ${unit}` : 'Set the question maximum above.');
                const value = Number(award.value);
                const hasText = row.querySelector('[data-open-accepted-text]').value.trim() !== '';
                const validAward = !award.validity.badInput && (award.value === '' ||
                    (validMaximum && Number.isFinite(value) && value >= 0 && value <= maximum));
                const invalid = hasText && !validAward;
                valid = valid && !invalid;
                award.setAttribute('aria-invalid', String(invalid));
                message.hidden = !invalid;
                text(message, invalid ? validMaximum
                    ? `Enter 0 to ${amount} points, or increase Maximum points above.`
                    : 'Set a valid question maximum before assigning points.' : '');
            });
        });
        panel.dataset.scoringValid = String(valid);
        panel.dataset.scoringReady = String(ready);
        return valid;
    }
    document.addEventListener('input', event => {
        if (event.target.matches?.('[data-question-maximum], [data-open-accepted-score], [data-open-accepted-text]'))
            refresh(event.target.closest('form[data-question-edit-form]'));
    }, true);
    function initialize() {
        document.querySelectorAll('[data-question-scoring]:not([data-scoring-ready="true"])').forEach(panel => refresh(panel.closest('form')));
    }
    window.questionScoring = { refresh, validate: refresh };
    document.addEventListener('DOMContentLoaded', initialize);
    new MutationObserver(initialize).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-exam-autosave-ready'] });
    initialize();
})();
