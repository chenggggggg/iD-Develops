(function () {
    "use strict";

    const editors = Array.from(document.querySelectorAll("[data-role-editor]"));

    function closeEditor(editor, restoreSelection) {
        const display = editor.querySelector("[data-role-display]");
        const controls = editor.querySelector("[data-role-controls]");
        const trigger = editor.querySelector("[data-role-edit]");
        const select = editor.querySelector("[data-role-select]");

        if (!display || !controls || !trigger || !select) {
            return;
        }

        if (restoreSelection) {
            select.value = select.dataset.originalRole || "";
        }

        controls.hidden = true;
        display.hidden = false;
        trigger.setAttribute("aria-expanded", "false");
    }

    editors.forEach(function (editor) {
        const display = editor.querySelector("[data-role-display]");
        const controls = editor.querySelector("[data-role-controls]");
        const trigger = editor.querySelector("[data-role-edit]");
        const cancel = editor.querySelector("[data-role-cancel]");
        const select = editor.querySelector("[data-role-select]");

        if (!display || !controls || !trigger || !cancel || !select) {
            return;
        }

        trigger.addEventListener("click", function () {
            editors.forEach(function (otherEditor) {
                if (otherEditor !== editor) {
                    closeEditor(otherEditor, true);
                }
            });

            display.hidden = true;
            controls.hidden = false;
            trigger.setAttribute("aria-expanded", "true");
            select.focus();
        });

        cancel.addEventListener("click", function () {
            closeEditor(editor, true);
            trigger.focus();
        });

        editor.addEventListener("keydown", function (event) {
            if (event.key !== "Escape" || controls.hidden) {
                return;
            }

            event.preventDefault();
            closeEditor(editor, true);
            trigger.focus();
        });
    });
})();
