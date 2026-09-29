(function (global) {
    "use strict";

    const defaultOptions = {
        height: 260,
        min_height: 180,
        menubar: false,
        plugins: "lists link table code paste",
        toolbar: "undo redo | bold italic underline | bullist numlist | link table | code | removeformat",
        paste_as_text: true,
        branding: false,
        promotion: false
    };

    function isTinyMceAvailable() {
        return Boolean(global.TinyMceHelpers && global.TinyMceHelpers.isAvailable());
    }

    function getEditor(textarea) {
        if (!textarea || !textarea.id || !global.tinymce) {
            return null;
        }

        return typeof global.tinymce.get === "function"
            ? global.tinymce.get(textarea.id)
            : null;
    }

    function syncPreview(field) {
        const textarea = field.querySelector("[data-exam-rich-textarea]");
        const preview = field.querySelector("[data-exam-rich-preview]");
        if (!textarea || !preview) {
            return;
        }

        const value = textarea.value || "";
        if (value.trim()) {
            preview.innerHTML = value;
            preview.classList.remove("is-empty");
        } else {
            preview.textContent = preview.getAttribute("data-placeholder") || "No text found...";
            preview.classList.add("is-empty");
        }
    }

    function closeEditor(field) {
        const textarea = field.querySelector("[data-exam-rich-textarea]");
        if (!textarea) {
            return;
        }
        const options = field.__examRichOptions || {};
        const valueBeforeClose = textarea.value;

        const editor = getEditor(textarea);
        if (editor) {
            editor.save();
            global.tinymce.remove(editor);
        }

        field.classList.remove("is-editing");
        syncPreview(field);
        if (textarea.value !== valueBeforeClose) {
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
        }
        if (typeof options.onClose === "function") {
            options.onClose(textarea, field);
        }
    }

    function openEditor(field, options) {
        const textarea = field.querySelector("[data-exam-rich-textarea]");
        if (!textarea || !isTinyMceAvailable()) {
            return;
        }

        if (!textarea.id) {
            textarea.id = `exam-rich-editor-${Math.random().toString(36).slice(2)}`;
        }

        field.classList.add("is-editing");

        if (getEditor(textarea)) {
            getEditor(textarea).focus();
            return;
        }

        global.TinyMceHelpers.initEditors([`#${CSS.escape(textarea.id)}`], Object.assign({}, defaultOptions, options || {}, {
            setup: function (editor) {
                const notifyChange = function () {
                    editor.save();
                    syncPreview(field);
                    textarea.dispatchEvent(new Event("input", { bubbles: true }));
                    if (typeof options?.onChange === "function") {
                        options.onChange(textarea, field, editor);
                    }
                };

                editor.on("init", function () {
                    editor.focus();
                });

                editor.on("input change undo redo paste cut setcontent", notifyChange);

                editor.on("blur", function () {
                    window.setTimeout(function () {
                        const active = document.activeElement;
                        const container = field.querySelector(".tox-tinymce");
                        const auxiliary = document.querySelector(".tox-tinymce-aux");
                        if ((container && container.contains(active)) || (auxiliary && auxiliary.contains(active))) {
                            return;
                        }

                        closeEditor(field);
                    }, 120);
                });
            }
        }));
    }

    function init(root, options) {
        const scope = root || document;
        scope.querySelectorAll("[data-exam-rich-field]").forEach(function (field) {
            const preview = field.querySelector("[data-exam-rich-preview]");
            if (!preview || field.dataset.examRichReady === "true") {
                return;
            }

            field.dataset.examRichReady = "true";
            field.__examRichOptions = options || {};
            syncPreview(field);

            preview.addEventListener("click", function () {
                openEditor(field, options);
            });

            preview.addEventListener("keydown", function (event) {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    openEditor(field, options);
                }
            });

            preview.setAttribute("tabindex", "0");
            preview.setAttribute("role", "button");
        });
    }

    function flush(root) {
        const scope = root || document;
        scope.querySelectorAll("[data-exam-rich-field]").forEach(closeEditor);
        if (global.tinymce && typeof global.tinymce.triggerSave === "function") {
            global.tinymce.triggerSave();
        }
    }

    global.ExamRichEditFields = {
        init,
        flush
    };
})(window);
