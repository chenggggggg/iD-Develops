(function () {
    "use strict";

    const root = document.querySelector("[data-product-credit-grants]");
    const list = root?.querySelector("[data-credit-grant-list]");
    const template = root?.querySelector("[data-credit-grant-template]");
    const addButton = root?.querySelector("[data-add-credit-grant]");
    if (!root || !list || !template || !addButton) return;

    function updateScope(row) {
        const scope = row.querySelector("[data-credit-scope]")?.value || "Global";
        const courseField = row.querySelector("[data-credit-course-field]");
        const classField = row.querySelector("[data-credit-class-field]");
        const courseSelect = courseField?.querySelector("select");
        const classSelect = classField?.querySelector("select");

        const usesCourse = scope === "Course";
        const usesClass = scope === "CourseClass";
        if (courseField) courseField.hidden = !usesCourse;
        if (classField) classField.hidden = !usesClass;
        if (courseSelect) {
            courseSelect.disabled = !usesCourse;
            courseSelect.required = usesCourse;
            if (!usesCourse) courseSelect.value = "";
        }
        if (classSelect) {
            classSelect.disabled = !usesClass;
            classSelect.required = usesClass;
            if (!usesClass) classSelect.value = "";
        }
    }

    function reindexRows() {
        list.querySelectorAll("[data-credit-grant-row]").forEach(function (row, index) {
            row.querySelectorAll("[name]").forEach(function (field) {
                field.name = field.name.replace(/CreditGrants\[\d+\]/, `CreditGrants[${index}]`);
            });
        });
    }

    function signalChange() {
        root.closest("form")?.dispatchEvent(new Event("input", { bubbles: true }));
    }

    function bindRow(row) {
        const scope = row.querySelector("[data-credit-scope]");
        scope?.addEventListener("change", function () {
            updateScope(row);
        });
        row.querySelector("[data-remove-credit-grant]")?.addEventListener("click", function () {
            row.remove();
            reindexRows();
            signalChange();
        });
        updateScope(row);
    }

    addButton.addEventListener("click", function () {
        const index = list.querySelectorAll("[data-credit-grant-row]").length;
        const wrapper = document.createElement("div");
        wrapper.innerHTML = template.innerHTML.replaceAll("__index__", String(index)).trim();
        const row = wrapper.firstElementChild;
        if (!row) return;
        list.appendChild(row);
        bindRow(row);
        row.querySelector("[data-credit-type]")?.focus();
        signalChange();
    });

    list.querySelectorAll("[data-credit-grant-row]").forEach(bindRow);
    reindexRows();
})();
