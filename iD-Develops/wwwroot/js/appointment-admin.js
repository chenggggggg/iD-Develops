(function () {
    "use strict";
    const root = document.querySelector("[data-appointment-admin]");
    if (!root) return;
    const editor = root.querySelector("[data-appointment-editor]");
    const field = name => editor.querySelector(`[data-field="${name}"]`);

    function open(data) {
        const item = data || { id: 0, name: "", durationMinutes: 60, requiredCreditTypeId: "", creditCost: 1, creditConsumptionPolicyId: "", teacherUserIds: [], isActive: true };
        field("id").value = item.id || 0;
        field("name").value = item.name || "";
        field("duration").value = item.durationMinutes || 60;
        field("credit").value = item.requiredCreditTypeId || "";
        field("cost").value = item.creditCost || 1;
        field("policy").value = item.creditConsumptionPolicyId || "";
        field("active").checked = item.isActive !== false;
        editor.querySelectorAll("[data-teacher-id]").forEach(input => {
            input.checked = (item.teacherUserIds || []).includes(input.dataset.teacherId);
        });
        editor.querySelector("[data-appointment-editor-title]").textContent = item.id ? "Edit appointment type" : "New appointment type";
        editor.hidden = false;
        field("name").focus();
    }

    root.querySelector("[data-appointment-new]")?.addEventListener("click", () => open(null));
    root.querySelector("[data-appointment-close]")?.addEventListener("click", () => { editor.hidden = true; });
    root.querySelectorAll("[data-appointment-edit]").forEach(button => button.addEventListener("click", () => open(JSON.parse(button.dataset.appointment))));
    if (!root.querySelector(".appointment-type-card") || root.querySelector(".validation-summary-errors")) open(null);
}());
