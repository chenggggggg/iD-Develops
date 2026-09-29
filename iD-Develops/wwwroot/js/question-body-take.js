(() => {
    if (window.__takeMcqDeselectBound) {
        return;
    }

    window.__takeMcqDeselectBound = true;

    document.addEventListener("click", (event) => {
        const label = event.target.closest("label[for]");
        if (!label) {
            return;
        }

        const forId = label.getAttribute("for");
        if (!forId) {
            return;
        }

        const radio = document.getElementById(forId);
        if (!(radio instanceof HTMLInputElement) || radio.type !== "radio" || radio.name !== "answer") {
            return;
        }

        if (radio.checked) {
            event.preventDefault();
            radio.checked = false;
            radio.dispatchEvent(new Event("change", { bubbles: true }));
        }
    });
})();
