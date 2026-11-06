(function () {
    "use strict";
    const root = document.querySelector("[data-appointment-admin]");
    if (!root) return;
    const editor = root.querySelector("[data-appointment-editor]");
    const field = name => editor.querySelector(`[data-field="${name}"]`);

    function updateCredits() {
        const required = field("requires-credits").checked;
        const settings = editor.querySelector("[data-credit-settings]");
        settings.hidden = !required;
        settings.disabled = !required;
        editor.querySelector("[data-free-description]").hidden = required;
        field("requires-credits").setAttribute("aria-expanded", String(required));
        if (required && Number(field("cost").value) < 1) field("cost").value = 1;
    }

    function open(data) {
        const item = data || { id: 0, name: "", durationMinutes: 60, requiredCreditTypeId: "", creditCost: 1, creditConsumptionPolicyId: "", teacherUserIds: [], isActive: true };
        field("id").value = item.id || 0;
        field("name").value = item.name || "";
        field("duration").value = item.durationMinutes || 60;
        field("credit").value = item.requiredCreditTypeId || "";
        field("cost").value = item.creditCost || 1;
        field("policy").value = item.creditConsumptionPolicyId || "";
        field("requires-credits").checked = item.requiredCreditTypeId != null && item.requiredCreditTypeId !== "";
        field("active").checked = item.isActive !== false;
        field("delivery").value = item.deliveryType || "Zoom";
        field("meeting-url").value = item.meetingUrl || "";
        field("location").value = item.location || "";
        editor.querySelectorAll("[data-teacher-id]").forEach(input => {
            input.checked = (item.teacherUserIds || []).includes(input.dataset.teacherId);
        });
        editor.querySelector("[data-appointment-editor-title]").textContent = item.id ? "Edit event type" : "New event type";
        updateCredits();
        editor.hidden = false;
        editor.scrollIntoView({ block: "nearest" });
        field("name").focus();
    }

    root.querySelector("[data-appointment-new]")?.addEventListener("click", () => open(null));
    root.querySelector("[data-appointment-close]")?.addEventListener("click", () => {
        editor.hidden = true;
        root.querySelector("[data-appointment-new]")?.focus();
    });
    root.querySelectorAll("[data-appointment-edit]").forEach(button => button.addEventListener("click", () => open(JSON.parse(button.dataset.appointment))));
    field("requires-credits").addEventListener("change", updateCredits);
    updateCredits();
}());
