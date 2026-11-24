(() => {
    "use strict";
    const text = (element, value) => {
        if (element && element.textContent !== value) element.textContent = value;
    };
    const amount = value => String(Number(value.toPrecision(12)));
    function refresh(form) {
        const panel = form?.querySelector('[data-question-scoring]');
        if (!panel) return true;
        const input = panel.querySelector('[data-question-maximum]');
        const ready = form.dataset.examAutosaveReady === 'true';
        const open = panel.dataset.scoringKind === 'Open';
        input.readOnly = open || !ready;
        input.setAttribute('aria-busy', String(!ready));
        let validAwards = true;
        let calculatedMaximum = 0;
        form.querySelectorAll('[data-open-field]').forEach((field, fieldIndex) => {
            const awards = [];
            field.querySelectorAll('[data-open-alternative]').forEach((row, index) => {
                const award = row.querySelector('[data-open-accepted-score]');
                const hint = row.querySelector('[data-open-award-limit]');
                const message = row.querySelector('[data-open-award-error]');
                const id = `open-award-${fieldIndex}-${index}`;
                hint.id = `${id}-limit`;
                message.id = `${id}-error`;
                award.setAttribute('aria-describedby', `${hint.id} ${message.id}`);
                award.removeAttribute('max');
                const value = Number(award.value);
                const hasText = row.querySelector('[data-open-accepted-text]').value.trim() !== '';
                const invalid = hasText && (award.validity.badInput || award.value.trim() === '' || !Number.isFinite(value) || value < 0);
                validAwards = validAwards && !invalid;
                if (hasText && !invalid) awards.push(value);
                award.setAttribute('aria-invalid', String(invalid));
                message.hidden = !invalid;
                text(message, invalid ? 'Enter a finite number of zero or greater.' : '');
                text(hint, 'Points for this alternative.');
            });
            const maximum = awards.length ? Math.max(...awards) : 1;
            calculatedMaximum += maximum;
            text(field.querySelector('[data-open-group-maximum]'), awards.length
                ? `Input maximum: ${amount(maximum)} ${maximum === 1 ? 'point' : 'points'}.`
                : 'Input maximum: 1 point pending an accepted answer.');
        });
        if (open) input.value = Number.isFinite(calculatedMaximum) ? amount(calculatedMaximum) : '';
        const maximum = Number(input.value);
        const validMaximum = input.value.trim() !== '' && Number.isFinite(maximum) && maximum > 0 && !input.validity.badInput;
        const error = panel.querySelector('[data-question-points-error]');
        input.setAttribute('aria-invalid', String(!validMaximum));
        error.hidden = validMaximum;
        text(error, validMaximum ? '' : open ? 'Answer inputs must have a finite total greater than zero.' : 'Enter a maximum greater than zero.');
        const points = validMaximum ? amount(maximum) : '\u2014';
        const unit = maximum === 1 ? 'point' : 'points';
        text(panel.querySelector('[data-question-award-summary]'), open
            ? `Calculated total: ${points} ${unit} from the answer inputs.`
            : `Correct: ${points} ${unit}. Incorrect or unanswered: 0.`);
        const valid = validMaximum && validAwards;
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
