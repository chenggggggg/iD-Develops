(function () {
    "use strict";

    const root = document.querySelector("[data-credit-product-settings]");
    if (!root) return;

    const scope = root.querySelector("[data-credit-product-scope]");
    const courseField = root.querySelector("[data-credit-product-course]");
    const classField = root.querySelector("[data-credit-product-class]");
    const summary = root.querySelector("[data-credit-rules-summary]");

    function updateScope() {
        const value = scope?.value || "Global";
        const courseSelect = courseField?.querySelector("select");
        const classSelect = classField?.querySelector("select");
        const usesCourse = value === "Course";
        const usesClass = value === "CourseClass";

        if (courseField) courseField.hidden = !usesCourse;
        if (classField) classField.hidden = !usesClass;
        if (courseSelect) {
            courseSelect.disabled = !usesCourse;
            courseSelect.required = usesCourse;
        }
        if (classSelect) {
            classSelect.disabled = !usesClass;
            classSelect.required = usesClass;
        }
    }

    function actionText(fieldName) {
        const value = root.querySelector(`[name="EditInput.CreditConfiguration.${fieldName}"]`)?.value;
        return value === "Return" ? "returned" : "consumed";
    }

    function updateSummary() {
        if (!summary) return;
        const timing = root.querySelector('[name="EditInput.CreditConfiguration.ConsumptionTiming"]')?.value;
        const hours = root.querySelector('[name="EditInput.CreditConfiguration.CancellationWindowHours"]')?.value || "0";
        const opening = timing === "OnAttendance"
            ? "The credit is charged after attendance is recorded."
            : "The credit is reserved as soon as the learner books.";
        summary.textContent = `${opening} It is ${actionText("AttendedAction")} after attendance, ${actionText("NoShowAction")} after a no-show, ${actionText("EarlyCancellationAction")} when cancelled at least ${hours} hours beforehand, ${actionText("LateCancellationAction")} after a late cancellation, and ${actionText("StaffCancellationAction")} when staff cancels.`;
    }

    scope?.addEventListener("change", updateScope);
    root.querySelectorAll("[data-credit-rule]").forEach(function (field) {
        field.addEventListener("input", updateSummary);
        field.addEventListener("change", updateSummary);
    });

    updateScope();
    updateSummary();
})();
