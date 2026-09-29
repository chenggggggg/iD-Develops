document.addEventListener("DOMContentLoaded", function () {
    const toggle = document.querySelector("[data-diagnostics-toggle]");
    const panel = document.getElementById("errorDiagnostics");

    if (!toggle || !panel) {
        return;
    }

    toggle.addEventListener("click", function (event) {
        event.preventDefault();

        const isExpanded = toggle.getAttribute("aria-expanded") === "true";
        toggle.setAttribute("aria-expanded", String(!isExpanded));
        toggle.textContent = isExpanded ? "View diagnostic details" : "Hide diagnostic details";
        panel.hidden = isExpanded;
    });
});
