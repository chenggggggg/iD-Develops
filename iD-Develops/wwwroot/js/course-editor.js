(function () {
    "use strict";

    const dataElement = document.getElementById("courseEditorData");
    const outline = document.querySelector("[data-course-editor-outline]");
    const main = document.querySelector("[data-course-editor-main]");
    const form = document.getElementById("course-edit-form");
    const jsonInput = document.querySelector("[data-course-editor-json]");
    if (!dataElement || !outline || !main || !form || !jsonInput) return;

    const state = JSON.parse(dataElement.textContent || "{}");
    const defaultFormAction = form.action;
    state.Sections = state.Sections || [];
    state.CreditTypes = state.CreditTypes || [];
    state.CreditPolicies = state.CreditPolicies || [];
    state.ExamOptions = state.ExamOptions || [];
    state.Learners = state.Learners || [];
    state.UnlockOverrides = state.UnlockOverrides || [];
    state.Sections.forEach(function (section) {
        section.Lectures = section.Lectures || [];
        section.Assignments = section.Assignments || [];
        section.Classes = section.Classes || [];
        section.Exams = section.Exams || [];
    });

    const courseId = Number(state.CourseId);
    const expanded = new Set(state.Sections.map(function (section) { return Number(section.Id); }));
    let selected = state.Sections.length ? { type: "section", id: Number(state.Sections[0].Id) } : { type: "course", id: courseId };
    let nextTemporaryId = Math.min(-1, ...collectIds().filter(function (id) { return id < 0; })) - 1;
    let dragged = null;
    let dirty = false;
    let allowConfirmedSubmit = false;
    let createExamPlaceholderId = null;

    const originalUnlockRules = new Map();
    state.Sections.forEach(function (section) {
        rememberUnlockRule("section", section);
        (section.Lectures || []).forEach(function (item) { rememberUnlockRule("lecture", item); });
        (section.Classes || []).forEach(function (item) { rememberUnlockRule("class", item); });
        (section.Exams || []).forEach(function (item) { rememberUnlockRule("exam", item); });
    });

    function rememberUnlockRule(type, item) {
        if (Number(item.Id) <= 0) return;
        originalUnlockRules.set(`${type}:${item.Id}`, {
            title: item.Title || `Untitled ${type}`,
            value: item.UnlockAfterValue ? Number(item.UnlockAfterValue) : null,
            unit: item.UnlockAfterValue ? Number(item.UnlockAfterUnit) || 1 : null
        });
    }

    function collectIds() {
        return state.Sections.flatMap(function (section) {
            return [section.Id]
                .concat((section.Lectures || []).map(function (item) { return item.Id; }))
                .concat((section.Assignments || []).map(function (item) { return item.Id; }))
                .concat((section.Classes || []).map(function (item) { return item.Id; }))
                .concat((section.Exams || []).map(function (item) { return item.Id; }));
        });
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function findSection(id) {
        return state.Sections.find(function (section) { return Number(section.Id) === Number(id); }) || null;
    }

    function collectionKey(type) {
        if (type === "lecture") return "Lectures";
        if (type === "assignment") return "Assignments";
        if (type === "exam") return "Exams";
        return "Classes";
    }

    function findTarget(target) {
        if (target.type === "course") return { item: state, parent: null };
        if (target.type === "section") return { item: findSection(target.id), parent: null };
        for (const section of state.Sections) {
            const key = collectionKey(target.type);
            const item = (section[key] || []).find(function (candidate) { return Number(candidate.Id) === Number(target.id); });
            if (item) return { item: item, parent: section };
        }
        return { item: null, parent: null };
    }

    function mixedChildren(section) {
        return (section.Lectures || []).map(function (item) { return { type: "lecture", item: item }; })
            .concat((section.Assignments || []).map(function (item) { return { type: "assignment", item: item }; }))
            .concat((section.Classes || []).map(function (item) { return { type: "class", item: item }; }))
            .concat((section.Exams || []).map(function (item) { return { type: "exam", item: item }; }))
            .sort(function (left, right) { return left.item.OrderNumber - right.item.OrderNumber || left.item.Id - right.item.Id; });
    }

    function normalizeOrders() {
        state.Sections.forEach(function (section, sectionIndex) {
            section.OrderNumber = sectionIndex;
            mixedChildren(section).forEach(function (entry, index) { entry.item.OrderNumber = index; });
        });
    }

    function markDirty() {
        dirty = true;
        form.classList.add("is-dirty");
    }

    function dragButton() {
        return `<button class="course-editor-drag" type="button" draggable="true" data-drag-handle aria-label="Drag to reorder" title="Drag to reorder"><span class="course-editor-grip" aria-hidden="true"><span></span><span></span><span></span><span></span><span></span><span></span></span></button>`;
    }

    function addContentOption(sectionId, type, icon, label, description) {
        return `<button class="group flex min-h-12 w-full items-center gap-3 rounded-md border-0 bg-transparent px-2.5 py-2 text-left text-slate-700 transition-colors hover:bg-slate-50 hover:text-slate-950 focus-visible:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[#b23a48]" type="button" data-add-child="${type}" data-section-id="${sectionId}">
            <span class="grid size-8 shrink-0 place-items-center rounded-md bg-slate-100 text-sm text-slate-600 group-hover:bg-white group-hover:text-[#b23a48]" aria-hidden="true"><i class="fa-solid ${icon}"></i></span>
            <span class="min-w-0 flex-1">
                <span class="block text-sm font-semibold leading-5">${label}</span>
                <span class="block text-xs leading-4 text-slate-500">${description}</span>
            </span>
            <i class="fa-solid fa-chevron-right text-[0.65rem] text-slate-400" aria-hidden="true"></i>
        </button>`;
    }

    function addContentMenu(section) {
        const menuId = `course-add-content-${section.Id}`;
        return `<div class="relative px-1 pb-1 pt-2" data-add-content-root>
            <button class="group flex min-h-11 w-full items-center gap-2.5 rounded-lg border border-dashed border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-600 shadow-sm transition-all duration-200 hover:border-[#b23a48] hover:bg-[#fff7f8] hover:text-[#b23a48] hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#b23a48]" type="button" data-add-content-toggle aria-controls="${menuId}" aria-expanded="false">
                <span class="grid size-7 place-items-center rounded-full bg-slate-100 text-xs transition-colors group-hover:bg-[#b23a48] group-hover:text-white"><i class="fa-solid fa-plus" aria-hidden="true"></i></span>
                <span class="flex-1 text-left">Add content</span>
                <i class="fa-solid fa-chevron-down text-[0.65rem] transition-transform" data-add-content-chevron aria-hidden="true"></i>
            </button>
            <div class="mt-2 grid gap-0.5 rounded-lg border border-slate-200 bg-white p-1.5 shadow-lg" id="${menuId}" data-add-content-menu hidden>
                ${addContentOption(section.Id, "lecture", "fa-circle-play", "Lecture", "Video, article, or learning material")}
                ${addContentOption(section.Id, "assignment", "fa-clipboard-check", "Assignment", "A task for students to complete")}
                ${addContentOption(section.Id, "class", "fa-calendar-days", "Live class", "A scheduled, bookable session")}
                ${addContentOption(section.Id, "exam", "fa-file-circle-check", "Exam", "An assessment with passing rules")}
            </div>
        </div>`;
    }

    function renderOutline() {
        normalizeOrders();
        if (!state.Sections.length) {
            outline.innerHTML = `<div class="course-editor-outline-empty"><i class="fa-solid fa-list" aria-hidden="true"></i><span>No sections yet</span></div>`;
            bindOutline();
            return;
        }

        outline.innerHTML = state.Sections.map(function (section) {
            const isExpanded = expanded.has(Number(section.Id));
            const children = mixedChildren(section);
            return `<section class="course-editor-section course-editor-tree-item ${selected.type === "section" && selected.id === Number(section.Id) ? "is-selected" : ""}" data-editor-item data-type="section" data-id="${section.Id}" draggable="true">
                <div class="course-editor-tree-row">
                    ${dragButton()}
                    <button class="course-editor-expand" type="button" data-section-toggle="${section.Id}" aria-label="${isExpanded ? "Collapse" : "Expand"} section"><i class="fa-solid fa-chevron-${isExpanded ? "down" : "right"}" aria-hidden="true"></i></button>
                    ${treeTitle(section, "section", "Section title")}
                    <button class="course-editor-remove" type="button" data-remove data-type="section" data-id="${section.Id}" aria-label="Remove section"><i class="fa-solid fa-trash" aria-hidden="true"></i></button>
                </div>
                <div class="course-editor-section-content" ${isExpanded ? "" : "hidden"}>
                    <div class="course-editor-children">
                        ${children.map(function (entry) { return childRow(section, entry.type, entry.item); }).join("")}
                        <div class="course-editor-drop-hint" data-child-drop-zone="${section.Id}">Drop lecture, assignment, class, or exam here</div>
                    </div>
                    ${addContentMenu(section)}
                </div>
            </section>`;
        }).join("");
        bindOutline();
    }

    function childRow(section, type, item) {
        const icon = type === "lecture" ? "fa-circle-play" : type === "assignment" ? "fa-clipboard-check" : type === "exam" ? "fa-file-circle-check" : "fa-calendar-days";
        const duration = type === "assignment" && Number(item.EstimatedDurationMinutes) > 0
            ? Number(item.EstimatedDurationMinutes)
            : 0;
        const meta = type === "class" ? `${Number(item.DurationMinutes) || 0} min` : type === "exam" ? (Number(item.PublishStatus) === 1 ? "Published" : Number(item.PublishStatus) === 2 ? "Archived" : "Draft") : `${duration} min`;
        const titleMarkup = type === "exam"
            ? `<span class="course-editor-inline-field"><span class="block min-w-0 truncate px-2 text-sm font-medium text-slate-800">${escapeHtml(item.Title)}</span></span>`
            : treeTitle(item, type, `${type} title`);
        return `<div class="course-editor-tree-item is-child ${selected.type === type && selected.id === Number(item.Id) ? "is-selected" : ""}" data-editor-item data-type="${type}" data-id="${item.Id}" data-parent-id="${section.Id}" draggable="true">
            <div class="course-editor-tree-row">
                ${dragButton()}
                <div class="course-editor-item-copy">
                    ${titleMarkup}
                    <span class="course-editor-item-meta"><i class="fa-solid ${icon}" aria-hidden="true"></i><span>${meta}</span></span>
                </div>
                <button class="course-editor-remove" type="button" data-remove data-type="${type}" data-id="${item.Id}" aria-label="Remove ${type}"><i class="fa-solid fa-trash" aria-hidden="true"></i></button>
            </div>
        </div>`;
    }

    function treeTitle(item, type, label) {
        return `<span class="course-editor-inline-field"><input value="${escapeHtml(item.Title)}" maxlength="200" data-tree-title data-type="${type}" data-id="${item.Id}" aria-label="${label}" /><i class="fa-solid fa-pencil" aria-hidden="true"></i></span>`;
    }

    function renderMain() {
        destroyRichEditor();
        const match = findTarget(selected);
        if (!match.item) {
            selected = { type: "course", id: courseId };
            main.innerHTML = renderCourse();
        } else if (selected.type === "course") {
            main.innerHTML = renderCourse();
        } else if (selected.type === "section") {
            main.innerHTML = renderSection(match.item);
        } else if (selected.type === "lecture") {
            main.innerHTML = renderLecture(match.item);
        } else if (selected.type === "class") {
            main.innerHTML = renderClass(match.item);
        } else if (selected.type === "exam") {
            main.innerHTML = renderExam(match.item);
        } else {
            main.innerHTML = renderAssignment(match.item);
        }
        bindMain();
        initializeRichEditor();
    }

    function heading(kind, icon, item, fallback) {
        const iconMarkup = icon ? `<i class="fa-solid ${icon}" aria-hidden="true"></i> ` : "";
        return `<section class="course-editor-heading"><p class="course-kicker">${iconMarkup}${kind}</p>
            <div class="course-editor-title-field"><input type="text" value="${escapeHtml(item.Title)}" maxlength="200" data-editor-field="Title" placeholder="${fallback}" /><i class="fa-solid fa-pencil" aria-hidden="true"></i></div></section>`;
    }

    function field(label, name, value, options) {
        const settings = options || {};
        const input = settings.textarea
            ? `<textarea rows="${settings.rows || 5}" data-editor-field="${name}" placeholder="${escapeHtml(settings.placeholder || "")}" ${settings.rich ? "data-rich-editor" : ""}>${escapeHtml(value)}</textarea>`
            : `<input type="${settings.type || "text"}" value="${escapeHtml(value)}" data-editor-field="${name}" placeholder="${escapeHtml(settings.placeholder || "")}" ${settings.min ? `min="${settings.min}"` : ""} ${settings.max ? `max="${settings.max}"` : ""} />`;
        return `<label class="course-editor-field ${settings.wide ? "is-wide" : ""}"><span>${label}</span><span class="course-editor-editable ${settings.textarea ? "has-textarea" : ""}">${input}</span></label>`;
    }

    function selectField(label, name, value, options, placeholder) {
        const optionMarkup = (options || []).map(function (option) {
            const selectedOption = Number(option.value) === Number(value);
            return `<option value="${option.value}" ${selectedOption ? "selected" : ""}>${escapeHtml(option.label)}</option>`;
        }).join("");
        return `<label class="course-editor-field"><span>${label}</span><span class="course-editor-editable"><select data-editor-field="${name}">${placeholder ? `<option value="">${escapeHtml(placeholder)}</option>` : ""}${optionMarkup}</select></span></label>`;
    }

    function checkboxField(label, name, checked) {
        return `<label class="course-editor-field course-editor-check-field"><span>${label}</span><span class="course-editor-checkbox-control"><input type="checkbox" data-editor-field="${name}" ${checked ? "checked" : ""} /><span aria-hidden="true"></span></span></label>`;
    }

    function unlockField(item) {
        const unit = Number(item.UnlockAfterUnit) || 1;
        return `<label class="course-editor-field"><span>Unlock after</span><span class="course-editor-delay-control">
            <input type="number" min="1" max="3650" value="${item.UnlockAfterValue ?? ""}" data-editor-field="UnlockAfterValue" placeholder="0" />
            <select data-editor-field="UnlockAfterUnit" aria-label="Unlock delay unit"><option value="1" ${unit === 1 ? "selected" : ""}>Days</option><option value="2" ${unit === 2 ? "selected" : ""}>Weeks</option><option value="3" ${unit === 3 ? "selected" : ""}>Months</option></select>
        </span></label>`;
    }

    function formatAmsterdamInput(utcValue) {
        if (!utcValue) return "";
        const parts = {};
        new Intl.DateTimeFormat("en-CA", {
            timeZone: "Europe/Amsterdam",
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            hourCycle: "h23"
        }).formatToParts(new Date(utcValue)).forEach(function (part) {
            if (part.type !== "literal") parts[part.type] = part.value;
        });
        return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
    }

    function unlockKindMatches(value, type) {
        const numericKinds = { section: 0, lecture: 1, assignment: 2, class: 3, exam: 4 };
        return String(value).toLowerCase() === type || Number(value) === numericKinds[type];
    }

    function learnerUnlockPanel(type, item) {
        if (Number(item.Id) <= 0) {
            return `<section class="mt-5 rounded-xl border border-slate-200 bg-slate-50 p-4">
                <strong class="block text-sm text-slate-900">Learner-specific access</strong>
                <p class="mb-0 mt-1 text-sm text-slate-500">Save this content before adding learner-specific access.</p>
            </section>`;
        }
        if (!state.Learners.length) return "";
        const kind = type.charAt(0).toUpperCase() + type.slice(1);
        const overrides = state.UnlockOverrides.filter(function (entry) {
            return unlockKindMatches(entry.ContentKind, type) && Number(entry.ContentId) === Number(item.Id);
        });
        const options = state.Learners.map(function (learner) {
            return `<option value="${escapeHtml(learner.UserId)}">${escapeHtml(learner.Name)} (${escapeHtml(learner.Email)})</option>`;
        }).join("");
        return `<section class="mt-5 rounded-xl border border-slate-200 bg-white p-4" data-unlock-panel data-content-kind="${kind}" data-content-id="${item.Id}">
            <div class="flex flex-wrap items-start justify-between gap-2">
                <div><strong class="block text-sm text-slate-900">Learner-specific access</strong><p class="mb-0 mt-1 text-xs text-slate-500">These changes save immediately. Times use Europe/Amsterdam.</p></div>
                <span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-semibold text-slate-600" data-unlock-count>${overrides.length} override${overrides.length === 1 ? "" : "s"}</span>
            </div>
            <div class="mt-4 grid gap-3 md:grid-cols-2">
                <label class="course-editor-field"><span>Learner</span><span class="course-editor-editable"><select data-unlock-user><option value="">Select learner</option>${options}</select></span></label>
                <label class="course-editor-field"><span>Unlock at (Amsterdam)</span><span class="course-editor-editable"><input type="datetime-local" data-unlock-at /></span></label>
            </div>
            <p class="mb-0 mt-2 text-xs text-slate-500" data-unlock-current>No learner selected.</p>
            <p class="mb-0 mt-2 text-sm text-red-700" data-unlock-error hidden></p>
            <div class="mt-3 flex flex-wrap gap-2">
                <button class="inline-flex min-h-9 items-center justify-center rounded-lg border border-[#b23a48] bg-[#b23a48] px-3 py-2 text-sm font-semibold text-white transition-colors hover:border-[#902f3b] hover:bg-[#902f3b] disabled:cursor-not-allowed disabled:opacity-50" type="button" data-unlock-save>Set unlock time</button>
                <button class="inline-flex min-h-9 items-center justify-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-700 transition-colors hover:border-[#b23a48] hover:text-[#b23a48] disabled:cursor-not-allowed disabled:opacity-50" type="button" data-unlock-now>Unlock now</button>
                <button class="inline-flex min-h-9 items-center justify-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-700 transition-colors hover:border-[#b23a48] hover:text-[#b23a48] disabled:cursor-not-allowed disabled:opacity-50" type="button" data-unlock-reset>Reset to default</button>
            </div>
        </section>`;
    }

    function renderCourse() {
        return `<section class="course-editor-heading"><p class="course-kicker"><i class="fa-solid fa-book-open" aria-hidden="true"></i> Course</p><div class="course-editor-title-field"><input type="text" value="${escapeHtml(state.Name)}" maxlength="200" data-editor-field="Name" placeholder="Course name" /><i class="fa-solid fa-pencil" aria-hidden="true"></i></div></section>
            <section class="course-editor-empty-preview"><i class="fa-solid fa-layer-group" aria-hidden="true"></i><strong>Course structure</strong></section>`;
    }

    function renderSection(section) {
        return `${heading("Section", null, section, "Untitled section")}
            <section class="course-editor-section-preview"><strong>${escapeHtml(section.Title || "Untitled section")}</strong><span>${section.Lectures.length} lectures &middot; ${section.Assignments.length} assignments &middot; ${section.Classes.length} classes &middot; ${section.Exams.length} exams</span></section>
            <section class="course-editor-fields">${unlockField(section)}</section>${learnerUnlockPanel("section", section)}`;
    }

    function renderLecture(lecture) {
        const type = Number(lecture.ContentType) || 0;
        const typeName = ["No content", "Video", "Article", "Mashup"][type] || "No content";
        return `${heading("Lecture", "fa-circle-play", lecture, "Untitled lecture")}
            <section class="course-editor-fields">
                ${unlockField(lecture)}
                ${field("Description", "Description", lecture.Description || "", { textarea: true, rows: 6, wide: true, placeholder: "Lecture description" })}
            </section>
            <section class="course-editor-resources">
                <div class="course-editor-resources-header"><div><p class="course-kicker">Content</p><h2>${typeName}</h2></div><button type="button" data-content-menu-toggle><i class="fa-solid fa-plus" aria-hidden="true"></i> ${type ? "Change content" : "Add content"}</button></div>
                <div class="course-content-type-menu" data-content-menu hidden>
                    <button type="button" data-content-type="1"><i class="fa-solid fa-circle-play" aria-hidden="true"></i> Video</button>
                    <button type="button" data-content-type="2"><i class="fa-solid fa-file-lines" aria-hidden="true"></i> Article</button>
                    <button type="button" data-content-type="3"><i class="fa-solid fa-layer-group" aria-hidden="true"></i> Mashup</button>
                </div>
                ${type === 1 || type === 3 ? uploadBox("video", "Upload video", ".mp4,.mov,.mkv,.avi,.webm,.mpeg,.mpg") : ""}
                ${type === 2 || type === 3 ? uploadBox("file", type === 2 ? "Upload article files" : "Upload PDF", type === 3 ? ".pdf" : ".pdf,.doc,.docx,.txt,.zip") : ""}
                ${renderFiles(lecture.SourceFiles || [], "Article files")}
            </section>${learnerUnlockPanel("lecture", lecture)}`;
    }

    function renderAssignment(assignment) {
        return `${heading("Assignment", "fa-clipboard-check", assignment, "Untitled assignment")}
            <section class="course-editor-fields">
                ${field("Description", "Description", assignment.Description || "", { textarea: true, rows: 5, wide: true, placeholder: "Assignment description" })}
                ${field("Estimated duration", "EstimatedDurationMinutes", assignment.EstimatedDurationMinutes || "", { type: "number", min: 1, max: 10080, placeholder: "Minutes" })}
                ${field("Instructions", "Instructions", assignment.Instructions || "", { textarea: true, rows: 10, wide: true, rich: true, placeholder: "Assignment instructions" })}
            </section>
            <section class="course-editor-resources">
                <div class="course-editor-resources-header"><div><p class="course-kicker">Instructional content</p><h2>Files</h2></div></div>
                ${uploadBox("video", "Upload instructional video", ".mp4,.mov,.mkv,.avi,.webm,.mpeg,.mpg")}
                ${uploadBox("file", "Upload supporting files", ".pdf,.zip,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.txt,.mp3,.m4a")}
                ${renderFiles(assignment.SupportingFiles || [], "Supporting files")}
            </section>${learnerUnlockPanel("assignment", assignment)}`;
    }

    function renderExam(exam) {
        const examOptions = state.ExamOptions.map(function (option) {
            return { value: option.Id, label: option.Name };
        });
        if (Number(exam.ExamId) > 0 && !examOptions.some(function (option) { return Number(option.value) === Number(exam.ExamId); })) {
            examOptions.unshift({
                value: exam.ExamId,
                label: `${exam.Title} (${Number(exam.PublishStatus) === 2 ? "Archived" : "Draft — already attached"})`
            });
        }
        const status = Number(exam.PublishStatus) === 1 ? "Published" : Number(exam.PublishStatus) === 2 ? "Archived" : "Draft";
        return `${heading("Exam", "fa-file-circle-check", exam, "Exam")}
            <div class="mb-5 rounded-xl border ${Number(exam.PublishStatus) === 1 ? "border-emerald-200 bg-emerald-50 text-emerald-900" : "border-amber-200 bg-amber-50 text-amber-900"} px-4 py-3 text-sm">
                <strong>${status}</strong>${Number(exam.PublishStatus) === 1 ? " · visible when this content unlocks" : " · not visible to students until published"}
            </div>
            <section class="course-editor-fields">
                ${selectField("Exam", "ExamId", exam.ExamId, examOptions, "No exam selected")}
                ${!Number(exam.ExamId) ? '<p class="col-span-full -mt-2 text-sm text-red-700" data-exam-selection-error>Select a published exam before saving.</p>' : ""}
                ${unlockField(exam)}
                ${checkboxField("Required for completion", "IsRequiredForCompletion", exam.IsRequiredForCompletion !== false)}
                ${field("Minimum passing score", "MinimumPassingScore", exam.MinimumPassingScore ?? 0, { type: "number", min: 0, max: 100000 })}
                ${selectField("When the learner does not pass", "FailureAction", exam.FailureAction || 2, [{ value: 1, label: "Allow course progress" }, { value: 2, label: "Require a passing score" }])}
            </section>
            <div class="mt-5 flex flex-wrap gap-2">
                ${Number(exam.ExamId) > 0 ? `<a class="inline-flex min-h-10 items-center justify-center gap-2 rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 no-underline transition-colors hover:border-[#b23a48] hover:text-[#b23a48]" href="/portal/examination/edit?examId=${Number(exam.ExamId)}"><i class="fa-solid fa-pen" aria-hidden="true"></i>Edit exam</a>` : ""}
                <button class="inline-flex min-h-10 items-center justify-center gap-2 rounded-lg border border-[#b23a48] bg-[#b23a48] px-4 py-2 text-sm font-semibold text-white shadow-sm transition-all hover:-translate-y-0.5 hover:border-[#902f3b] hover:bg-[#902f3b] hover:shadow-md" type="button" data-create-new-exam data-section-id="${findTarget(selected).parent?.Id || ""}"><i class="fa-solid fa-plus" aria-hidden="true"></i>Create and attach new exam</button>
            </div>${learnerUnlockPanel("exam", exam)}`;
    }

    function renderClass(courseClass) {
        const format = Number(courseClass.Format) || 1;
        const bookingAccess = Number(courseClass.BookingAccess) || 1;
        const bookingEligibility = Number(courseClass.BookingEligibility) || 1;
        const usesCredit = bookingAccess !== 1;
        const creditProducts = (state.CreditProducts || []).map(function (item) { return { value: item.Id, label: `${item.Name}${item.IsActive ? "" : " (unavailable for new sales)"}` }; });
        const upcomingSessionCount = Number(courseClass.UpcomingSessionCount) || 0;
        const sessionStatus = upcomingSessionCount === 0
            ? "No sessions scheduled"
            : `${upcomingSessionCount} upcoming session${upcomingSessionCount === 1 ? "" : "s"}`;
        const sessionHelp = upcomingSessionCount === 0
            ? "Add a date and time so learners can book this class."
            : "Review the available dates or add another session.";
        const sessionAction = Number(courseClass.Id) > 0
            ? `<a href="/portal/calendar/roster?courseClassId=${Number(courseClass.Id)}&mode=single#schedule-session" class="inline-flex items-center justify-center gap-2 rounded-lg bg-sky-700 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-sky-800"><i class="fa-solid fa-calendar-plus" aria-hidden="true"></i>${upcomingSessionCount === 0 ? "Add session" : "Manage sessions"}</a>`
            : `<span class="text-xs text-slate-500">Save the course before adding sessions.</span>`;
        return `${heading("Class", "fa-calendar-days", courseClass, "Untitled class")}
            <section class="course-editor-class-preview"><i class="fa-solid fa-calendar-days" aria-hidden="true"></i><strong>${escapeHtml(courseClass.Title || "Untitled class")}</strong><span>${Number(courseClass.DurationMinutes) || 0} min · ${format === 2 ? "One-to-one" : "Group"}</span></section>
            <div class="grid gap-4 py-4">
                <details class="group rounded-2xl border border-slate-200 bg-white shadow-sm" open>
                    <summary class="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4"><span><strong class="block text-sm text-slate-900">Class details</strong><small class="mt-1 block text-xs text-slate-500">The format, duration, and capacity of each session.</small></span><i class="fa-solid fa-chevron-down text-slate-400 transition-transform group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields !border-b-0 border-t border-slate-100 !px-5 !py-5">
                        ${selectField("Session type", "Format", format, [{ value: 1, label: "Group session" }, { value: 2, label: "One-to-one session" }])}
                        ${field("Duration", "DurationMinutes", courseClass.DurationMinutes || 60, { type: "number", min: 5, max: 1440, placeholder: "Minutes" })}
                        ${format === 1 ? field("Maximum learners per session", "Capacity", courseClass.Capacity || 1, { type: "number", min: 1, max: 10000, placeholder: "Seats" }) : ""}
                    </section>
                </details>
                <details class="group rounded-2xl border border-slate-200 bg-white shadow-sm">
                    <summary class="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4"><span><strong class="block text-sm text-slate-900">Course progress</strong><small class="mt-1 block text-xs text-slate-500">Choose when this class unlocks and whether attendance is required.</small></span><i class="fa-solid fa-chevron-down text-slate-400 transition-transform group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields !border-b-0 border-t border-slate-100 !px-5 !py-5">
                        ${unlockField(courseClass)}
                        ${checkboxField("Required for course completion", "IsRequiredForCompletion", Boolean(courseClass.IsRequiredForCompletion))}
                        <p class="col-span-full m-0 text-xs leading-5 text-slate-500">Required classes are completed automatically after the learner is marked attended. No-shows do not count.</p>
                    </section>
                </details>
                <details class="group rounded-2xl border border-slate-200 bg-white shadow-sm">
                    <summary class="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4"><span><strong class="block text-sm text-slate-900">Booking</strong><small class="mt-1 block text-xs text-slate-500">Control who can book and what entitlement they need.</small></span><i class="fa-solid fa-chevron-down text-slate-400 transition-transform group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields !border-b-0 border-t border-slate-100 !px-5 !py-5">
                        ${selectField("Who can book?", "BookingAccess", bookingAccess, [{ value: 1, label: "Enrolled learners · included with course" }, { value: 2, label: "Anyone with the required Credit Product" }, { value: 3, label: "Enrolled learners · Credit Product required" }])}
                        ${selectField("Booking opens", "BookingEligibility", bookingEligibility, [{ value: 1, label: "When this class unlocks" }, { value: 2, label: "When the previous section unlocks" }])}
                        ${checkboxField("Show in student self-booking", "IsVisibleForStudentBooking", courseClass.IsVisibleForStudentBooking !== false)}
                        ${!usesCredit ? field("Sessions included per learner", "EnrollmentBookingLimit", courseClass.EnrollmentBookingLimit || "", { type: "number", min: 1, max: 100000, placeholder: "Unlimited" }) : ""}
                        ${usesCredit ? selectField("Required Credit Product", "RequiredCreditProductId", courseClass.RequiredCreditProductId, creditProducts, "Select Credit Product") : ""}
                        ${usesCredit ? field("Credits per booking", "CreditCost", courseClass.CreditCost || 1, { type: "number", min: 1, max: 100000 }) : ""}
                        ${usesCredit ? '<p class="col-span-full m-0 text-xs leading-5 text-slate-500">Attendance, no-show, and cancellation rules come from the selected Credit Product.</p>' : ""}
                    </section>
                </details>
                <section class="flex flex-col gap-4 rounded-2xl border border-slate-200 bg-slate-50 p-5 sm:flex-row sm:items-center sm:justify-between">
                    <span><strong class="block text-sm text-slate-900">${sessionStatus}</strong><small class="mt-1 block text-xs text-slate-500">${sessionHelp}</small></span>
                    ${sessionAction}
                </section>
            </div>${learnerUnlockPanel("class", courseClass)}`;
    }

    function toDateTimeLocal(value) {
        if (!value) return "";
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return "";
        const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
        return local.toISOString().slice(0, 16);
    }

    function formatClassSchedule(value) {
        if (!value) return "Not scheduled";
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return "Not scheduled";
        return escapeHtml(new Intl.DateTimeFormat(undefined, {
            dateStyle: "medium",
            timeStyle: "short"
        }).format(date));
    }

    function uploadBox(kind, label, accept) {
        const enabled = kind === "video" ? document.body.dataset.courseVideoUploads !== "false" : document.body.dataset.courseFileUploads !== "false";
        return `<div class="course-editor-upload ${enabled ? "" : "is-disabled"}"><label><span>${label}</span><input type="file" accept="${accept}" data-upload-${kind} ${enabled ? "" : "disabled"} /></label><span data-upload-status>${enabled ? "" : "Uploads are not configured"}</span></div>`;
    }

    function renderFiles(files, title) {
        if (!files.length) return "";
        return `<div class="course-editor-resource-list"><p class="course-kicker">${title}</p>${files.map(function (file, index) {
            return `<div class="course-editor-resource"><i class="fa-solid fa-paperclip" aria-hidden="true"></i><span>${escapeHtml(file.Name)}</span><button type="button" data-remove-file="${index}" aria-label="Remove file"><i class="fa-solid fa-trash" aria-hidden="true"></i></button></div>`;
        }).join("")}</div>`;
    }

    function bindOutline() {
        outline.querySelectorAll("[data-add-content-toggle]").forEach(function (button) {
            button.addEventListener("click", function () {
                const root = button.closest("[data-add-content-root]");
                const menu = root?.querySelector("[data-add-content-menu]");
                if (!menu) return;
                const shouldOpen = menu.hidden;
                outline.querySelectorAll("[data-add-content-menu]").forEach(function (candidate) {
                    candidate.hidden = true;
                    candidate.closest("[data-add-content-root]")?.querySelector("[data-add-content-toggle]")?.setAttribute("aria-expanded", "false");
                    candidate.closest("[data-add-content-root]")?.querySelector("[data-add-content-chevron]")?.classList.remove("rotate-180");
                });
                menu.hidden = !shouldOpen;
                button.setAttribute("aria-expanded", String(shouldOpen));
                root.querySelector("[data-add-content-chevron]")?.classList.toggle("rotate-180", shouldOpen);
                if (shouldOpen) menu.querySelector("button")?.focus();
            });
        });
        outline.querySelectorAll("[data-section-toggle]").forEach(function (button) {
            button.addEventListener("click", function () { const id = Number(button.dataset.sectionToggle); expanded.has(id) ? expanded.delete(id) : expanded.add(id); renderOutline(); });
        });
        outline.querySelectorAll("[data-editor-item]").forEach(function (element) {
            element.querySelector(":scope > .course-editor-tree-row")?.addEventListener("click", function (event) {
                if (event.target.closest("button,input")) return;
                selected = elementTarget(element); render();
            });
            bindDrag(element);
        });
        outline.querySelectorAll("[data-tree-title]").forEach(function (input) {
            input.addEventListener("focus", function () {
                selected = { type: input.dataset.type, id: Number(input.dataset.id) };
                updateOutlineSelection(selected);
                renderMain();
            });
            input.addEventListener("input", function () {
                const target = { type: input.dataset.type, id: Number(input.dataset.id) };
                const match = findTarget(target);
                match.item.Title = input.value;
                if (selected.type === target.type && selected.id === target.id) {
                    const mainTitle = main.querySelector('[data-editor-field="Title"]');
                    if (mainTitle) mainTitle.value = input.value;
                    const previewTitle = main.querySelector(".course-editor-section-preview strong");
                    if (previewTitle) previewTitle.textContent = input.value || `Untitled ${target.type}`;
                }
                markDirty();
            });
        });
        outline.querySelectorAll("[data-remove]").forEach(function (button) {
            button.addEventListener("click", function () { removeItem({ type: button.dataset.type, id: Number(button.dataset.id) }); });
        });
        outline.querySelectorAll("[data-add-child]").forEach(function (button) {
            button.addEventListener("click", function () { addChild(Number(button.dataset.sectionId), button.dataset.addChild); });
        });
        outline.querySelectorAll("[data-child-drop-zone]").forEach(bindDropZone);
    }

    function updateOutlineSelection(target) {
        outline.querySelectorAll("[data-editor-item].is-selected").forEach(function (item) {
            item.classList.remove("is-selected");
        });
        outline.querySelector(`[data-editor-item][data-type="${target.type}"][data-id="${target.id}"]`)?.classList.add("is-selected");
    }

    function bindMain() {
        main.querySelectorAll("[data-editor-field]").forEach(function (input) {
            input.addEventListener("input", function () { updateField(input); });
            input.addEventListener("change", function () { updateField(input); });
        });
        main.querySelector("[data-content-menu-toggle]")?.addEventListener("click", function () { const menu = main.querySelector("[data-content-menu]"); menu.hidden = !menu.hidden; });
        main.querySelectorAll("[data-content-type]").forEach(function (button) {
            button.addEventListener("click", function () { const item = findTarget(selected).item; item.ContentType = Number(button.dataset.contentType); markDirty(); renderMain(); });
        });
        main.querySelectorAll("[data-remove-file]").forEach(function (button) {
            button.addEventListener("click", function () { const item = findTarget(selected).item; const key = selected.type === "lecture" ? "SourceFiles" : "SupportingFiles"; item[key].splice(Number(button.dataset.removeFile), 1); markDirty(); renderMain(); });
        });
        main.querySelector("[data-upload-video]")?.addEventListener("change", function (event) { uploadVideo(event.target); });
        main.querySelector("[data-upload-file]")?.addEventListener("change", function (event) { uploadFile(event.target); });
        main.querySelector("[data-create-new-exam]")?.addEventListener("click", function () {
            const match = findTarget(selected);
            if (match.parent) startCreateExam(match.parent);
        });
        main.querySelectorAll("[data-unlock-panel]").forEach(bindLearnerUnlockPanel);
    }

    function bindLearnerUnlockPanel(panel) {
        const userSelect = panel.querySelector("[data-unlock-user]");
        const unlockInput = panel.querySelector("[data-unlock-at]");
        const current = panel.querySelector("[data-unlock-current]");

        function selectedOverride() {
            return state.UnlockOverrides.find(function (entry) {
                return entry.UserId === userSelect.value &&
                    unlockKindMatches(entry.ContentKind, panel.dataset.contentKind.toLowerCase()) &&
                    Number(entry.ContentId) === Number(panel.dataset.contentId);
            });
        }

        userSelect.addEventListener("change", function () {
            const override = selectedOverride();
            unlockInput.value = override ? formatAmsterdamInput(override.UnlockAtUtc) : "";
            current.textContent = !userSelect.value
                ? "No learner selected."
                : override
                    ? `Current override: ${new Date(override.UnlockAtUtc).toLocaleString("en-GB", { timeZone: "Europe/Amsterdam", dateStyle: "medium", timeStyle: "short" })}`
                    : "This learner follows the default unlock schedule.";
        });

        panel.querySelector("[data-unlock-save]").addEventListener("click", function () { saveLearnerUnlock(panel, "set"); });
        panel.querySelector("[data-unlock-now]").addEventListener("click", function () { saveLearnerUnlock(panel, "now"); });
        panel.querySelector("[data-unlock-reset]").addEventListener("click", function () { saveLearnerUnlock(panel, "reset"); });
    }

    async function saveLearnerUnlock(panel, mode) {
        const userSelect = panel.querySelector("[data-unlock-user]");
        const unlockInput = panel.querySelector("[data-unlock-at]");
        const error = panel.querySelector("[data-unlock-error]");
        const buttons = panel.querySelectorAll("button");
        error.hidden = true;

        if (!userSelect.value) {
            error.textContent = "Select a learner first.";
            error.hidden = false;
            return;
        }
        if (mode === "set" && !unlockInput.value) {
            error.textContent = "Choose an unlock date and time.";
            error.hidden = false;
            return;
        }

        buttons.forEach(function (button) { button.disabled = true; });
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const body = new URLSearchParams({
                learnerUserId: userSelect.value,
                contentKind: panel.dataset.contentKind,
                contentId: panel.dataset.contentId,
                unlockAtAmsterdam: mode === "set" ? unlockInput.value : "",
                unlockNow: String(mode === "now"),
                resetToDefault: String(mode === "reset"),
                __RequestVerificationToken: token
            });
            const response = await fetch(`${window.location.pathname}?handler=SetContentUnlock&courseId=${courseId}`, {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" },
                body: body
            });
            const json = await response.json().catch(function () { return {}; });
            if (!response.ok || json.success === false) throw new Error(json.errorMessage || "The unlock time could not be saved.");

            state.UnlockOverrides = state.UnlockOverrides.filter(function (entry) {
                return !(entry.UserId === userSelect.value &&
                    unlockKindMatches(entry.ContentKind, panel.dataset.contentKind.toLowerCase()) &&
                    Number(entry.ContentId) === Number(panel.dataset.contentId));
            });
            if (!json.resetToDefault && json.unlockAtUtc) {
                state.UnlockOverrides.push({
                    UserId: userSelect.value,
                    ContentKind: panel.dataset.contentKind,
                    ContentId: Number(panel.dataset.contentId),
                    UnlockAtUtc: json.unlockAtUtc
                });
            }
            bindMainAfterUnlockSave(panel, userSelect.value);
        } catch (requestError) {
            error.textContent = requestError.message || "The unlock time could not be saved.";
            error.hidden = false;
        } finally {
            buttons.forEach(function (button) { button.disabled = false; });
        }
    }

    function bindMainAfterUnlockSave(panel, userId) {
        const kind = panel.dataset.contentKind.toLowerCase();
        const overrides = state.UnlockOverrides.filter(function (entry) {
            return unlockKindMatches(entry.ContentKind, kind) && Number(entry.ContentId) === Number(panel.dataset.contentId);
        });
        const override = overrides.find(function (entry) { return entry.UserId === userId; });
        panel.querySelector("[data-unlock-count]").textContent = `${overrides.length} override${overrides.length === 1 ? "" : "s"}`;
        panel.querySelector("[data-unlock-at]").value = override ? formatAmsterdamInput(override.UnlockAtUtc) : "";
        panel.querySelector("[data-unlock-current]").textContent = override
            ? `Current override: ${new Date(override.UnlockAtUtc).toLocaleString("en-GB", { timeZone: "Europe/Amsterdam", dateStyle: "medium", timeStyle: "short" })}`
            : "This learner follows the default unlock schedule.";
    }

    function startCreateExam(section) {
        normalizeOrders();
        createExamPlaceholderId = selected.type === "exam" && !findTarget(selected).item.ExamId ? selected.id : null;
        function addValue(name, value) {
            let input = form.querySelector(`input[name="${name}"]`);
            if (!input) {
                input = document.createElement("input");
                input.type = "hidden";
                input.name = name;
                form.appendChild(input);
            }
            input.value = String(value);
        }
        addValue("CreateExamSectionId", Number(section.Id));
        addValue("CreateExamSectionOrder", Number(section.OrderNumber));
        form.action = `${window.location.pathname}?handler=SaveAndCreateExam&courseId=${courseId}`;
        form.requestSubmit();
    }

    function updateField(input) {
        const item = findTarget(selected).item;
        const numberFields = new Set(["UnlockAfterValue", "UnlockAfterUnit", "EstimatedDurationMinutes", "Format", "DurationMinutes", "Capacity", "BookingAccess", "BookingEligibility", "EnrollmentBookingLimit", "RequiredCreditProductId", "RequiredCreditTypeId", "CreditCost", "CreditConsumptionPolicyId", "ExamId", "MinimumPassingScore", "FailureAction"]);
        item[input.dataset.editorField] = input.type === "checkbox"
            ? input.checked
            : input.dataset.editorField === "MeetingAtUtc"
            ? (input.value ? new Date(input.value).toISOString() : null)
            : numberFields.has(input.dataset.editorField)
                ? (input.value ? Number(input.value) : null)
                : input.value;
        if (["Title", "Name", "EstimatedDurationMinutes", "MeetingAtUtc", "DurationMinutes"].includes(input.dataset.editorField)) renderOutline();
        if (input.dataset.editorField === "Format") {
            if (Number(item.Format) === 2) item.Capacity = 1;
            renderMain();
        }
        if (input.dataset.editorField === "BookingAccess") {
            if (Number(item.BookingAccess) === 1) {
                item.RequiredCreditTypeId = null;
                item.RequiredCreditProductId = null;
                item.CreditConsumptionPolicyId = null;
                item.CreditCost = 1;
            } else {
                item.EnrollmentBookingLimit = null;
            }
            renderMain();
        }
        if (input.dataset.editorField === "ExamId") {
            const option = state.ExamOptions.find(function (candidate) { return Number(candidate.Id) === Number(item.ExamId); });
            if (option) {
                item.Title = option.Name;
                item.PublishStatus = option.PublishStatus;
            }
            render();
        }
        markDirty();
    }

    function initializeRichEditor() {
        const textarea = main.querySelector("[data-rich-editor]");
        if (!textarea || !window.TinyMceHelpers) return;
        textarea.id = `course-rich-${selected.type}-${selected.id}`;
        window.TinyMceHelpers.initEditors([`#${CSS.escape(textarea.id)}`], { height: 320, min_height: 240, plugins: "autolink link lists table", toolbar: "undo redo | blocks | bold italic underline | bullist numlist | link table | removeformat", paste_as_text: false, setup: function (editor) {
            editor.on("change input undo redo", function () { findTarget(selected).item.Instructions = editor.getContent(); markDirty(); });
        }});
    }

    function destroyRichEditor() {
        const ids = Array.from(main.querySelectorAll("[data-rich-editor]"))
            .map(function (textarea) { return textarea.id; })
            .filter(Boolean);
        window.TinyMceHelpers?.removeEditorsById(ids);
    }

    function addChild(sectionId, type) {
        const section = findSection(sectionId); if (!section) return;
        const item = type === "lecture"
            ? { Id: nextTemporaryId--, Title: "Untitled lecture", OrderNumber: mixedChildren(section).length, Description: "", ContentType: 0, VideoReference: null, UnlockAfterValue: null, UnlockAfterUnit: 1, SourceFiles: [] }
            : type === "assignment"
                ? { Id: nextTemporaryId--, Title: "Untitled assignment", OrderNumber: mixedChildren(section).length, Description: "", EstimatedDurationMinutes: null, Instructions: "", InstructionalVideoReference: null, SupportingFiles: [] }
                : type === "exam"
                    ? { Id: nextTemporaryId--, ExamId: null, Title: "Exam", PublishStatus: 0, OrderNumber: mixedChildren(section).length, UnlockAfterValue: null, UnlockAfterUnit: 1, IsRequiredForCompletion: true, MinimumPassingScore: 0, FailureAction: 2 }
                    : { Id: nextTemporaryId--, Title: "Untitled class", OrderNumber: mixedChildren(section).length, UnlockAfterValue: null, UnlockAfterUnit: 1, MeetingLink: "", MeetingAtUtc: null, Format: 1, DurationMinutes: 60, Capacity: 1, BookingAccess: 1, BookingEligibility: 1, IsVisibleForStudentBooking: true, IsRequiredForCompletion: false, EnrollmentBookingLimit: null, RequiredCreditProductId: null, RequiredCreditTypeId: null, CreditCost: 1, CreditConsumptionPolicyId: null, UpcomingSessionCount: 0 };
        section[collectionKey(type)].push(item);
        expanded.add(sectionId); selected = { type: type, id: item.Id }; markDirty(); render();
    }

    function addSection() {
        const section = { Id: nextTemporaryId--, Title: "Untitled section", OrderNumber: state.Sections.length, UnlockAfterValue: null, UnlockAfterUnit: 1, Lectures: [], Assignments: [], Classes: [], Exams: [] };
        state.Sections.push(section); expanded.add(section.Id); selected = { type: "section", id: section.Id }; markDirty(); render();
    }

    function removeItem(target) {
        if (!window.confirm(`Remove this ${target.type}?`)) return;
        if (target.type === "section") state.Sections = state.Sections.filter(function (item) { return Number(item.Id) !== target.id; });
        else state.Sections.forEach(function (section) { const key = collectionKey(target.type); section[key] = section[key].filter(function (item) { return Number(item.Id) !== target.id; }); });
        selected = { type: "course", id: courseId }; markDirty(); render();
    }

    function elementTarget(element) {
        return { type: element.dataset.type, id: Number(element.dataset.id), parentId: element.dataset.parentId ? Number(element.dataset.parentId) : null };
    }

    function bindDrag(element) {
        let armed = false;
        element.querySelector(":scope > .course-editor-tree-row [data-drag-handle]")?.addEventListener("pointerdown", function () { armed = true; });
        element.addEventListener("dragstart", function (event) { if (event.target.closest("[data-editor-item]") !== element || !armed) { event.preventDefault(); return; } event.stopPropagation(); dragged = elementTarget(element); element.classList.add("is-dragging"); });
        element.addEventListener("dragover", function (event) { if (!dragged || dragged.id === Number(element.dataset.id)) return; const target = elementTarget(element); if ((dragged.type === "section") !== (target.type === "section")) return; event.preventDefault(); event.stopPropagation(); element.classList.add("is-drag-target"); });
        element.addEventListener("dragleave", function () { element.classList.remove("is-drag-target"); });
        element.addEventListener("drop", function (event) { const target = elementTarget(element); if (!dragged || (dragged.type === "section") !== (target.type === "section")) return; event.preventDefault(); event.stopPropagation(); moveBefore(dragged, target); });
        element.addEventListener("dragend", finishDrag);
    }

    function bindDropZone(zone) {
        zone.addEventListener("dragover", function (event) { if (dragged && dragged.type !== "section") { event.preventDefault(); event.stopPropagation(); zone.classList.add("is-drag-target"); } });
        zone.addEventListener("dragleave", function () { zone.classList.remove("is-drag-target"); });
        zone.addEventListener("drop", function (event) { if (!dragged || dragged.type === "section") return; event.preventDefault(); moveToSectionEnd(dragged, Number(zone.dataset.childDropZone)); });
    }

    function takeChild(target) {
        for (const section of state.Sections) {
            const key = collectionKey(target.type);
            const index = section[key].findIndex(function (item) { return Number(item.Id) === target.id; });
            if (index >= 0) return section[key].splice(index, 1)[0];
        }
        return null;
    }

    function moveBefore(source, target) {
        if (source.type === "section") {
            const from = state.Sections.findIndex(function (item) { return Number(item.Id) === source.id; });
            const item = state.Sections.splice(from, 1)[0];
            state.Sections.splice(state.Sections.findIndex(function (entry) { return Number(entry.Id) === target.id; }), 0, item);
        } else {
            const item = takeChild(source); const targetMatch = findTarget(target); if (!item || !targetMatch.parent) return;
            item.OrderNumber = targetMatch.item.OrderNumber - 0.5;
            targetMatch.parent[collectionKey(source.type)].push(item);
        }
        markDirty(); finishDrag(); render();
    }

    function moveToSectionEnd(source, sectionId) {
        const item = takeChild(source); const section = findSection(sectionId); if (!item || !section) return;
        item.OrderNumber = mixedChildren(section).length;
        section[collectionKey(source.type)].push(item);
        markDirty(); finishDrag(); render();
    }

    function finishDrag() {
        dragged = null;
        outline.querySelectorAll(".is-dragging,.is-drag-target").forEach(function (item) { item.classList.remove("is-dragging", "is-drag-target"); });
    }

    async function uploadVideo(input) {
        const file = input.files?.[0]; if (!file || !canUpload(input)) return;
        if (!window.confirm(`Upload ${file.name} to Cloudflare Stream?`)) { input.value = ""; return; }
        const status = input.closest(".course-editor-upload").querySelector("[data-upload-status]");
        try {
            status.textContent = "Preparing upload...";
            const upload = await createUpload("CreateVideoUpload", file);
            const body = new FormData(); body.append("file", file);
            status.textContent = "Uploading video...";
            const response = await fetch(upload.uploadUrl, { method: "POST", body: body });
            if (!response.ok) throw new Error("Video upload failed.");
            status.textContent = "Video uploaded."; window.location.reload();
        } catch (error) { status.textContent = error.message || "Upload failed."; input.value = ""; }
    }

    async function uploadFile(input) {
        const file = input.files?.[0]; if (!file || !canUpload(input)) return;
        if (!window.confirm(`Upload ${file.name} to object storage?`)) { input.value = ""; return; }
        const status = input.closest(".course-editor-upload").querySelector("[data-upload-status]");
        try {
            status.textContent = "Preparing upload...";
            const upload = await createUpload("CreateFileUpload", file);
            const headers = { "Content-Type": file.type || "application/octet-stream" };
            if (upload.cacheControl) headers["Cache-Control"] = upload.cacheControl;
            status.textContent = "Uploading file...";
            const response = await fetch(upload.putUrl, { method: "PUT", headers: headers, body: file });
            if (!response.ok) throw new Error("File upload failed.");
            status.textContent = "File uploaded."; window.location.reload();
        } catch (error) { status.textContent = error.message || "Upload failed."; input.value = ""; }
    }

    function canUpload(input) {
        const item = findTarget(selected).item;
        if (dirty || !item || Number(item.Id) <= 0) {
            input.closest(".course-editor-upload").querySelector("[data-upload-status]").textContent = "Save course changes before uploading.";
            input.value = ""; return false;
        }
        return true;
    }

    async function createUpload(handler, file) {
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
        const body = new URLSearchParams({ itemType: selected.type, itemId: String(selected.id), fileName: file.name, fileSize: String(file.size), contentType: file.type || "application/octet-stream", __RequestVerificationToken: token });
        const response = await fetch(`${window.location.pathname}?handler=${handler}&courseId=${courseId}`, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" }, body: body });
        const json = await response.json().catch(function () { return {}; });
        if (!response.ok) throw new Error(json.errorMessage || "Upload could not be started.");
        return json;
    }

    function render() { renderOutline(); renderMain(); }

    function changedUnlockRules() {
        const changes = [];
        state.Sections.forEach(function (section) {
            [{ type: "section", item: section }]
                .concat((section.Lectures || []).map(function (item) { return { type: "lecture", item: item }; }))
                .concat((section.Classes || []).map(function (item) { return { type: "class", item: item }; }))
                .concat((section.Exams || []).map(function (item) { return { type: "exam", item: item }; }))
                .forEach(function (entry) {
                    const original = originalUnlockRules.get(`${entry.type}:${entry.item.Id}`);
                    if (!original) return;
                    const value = entry.item.UnlockAfterValue ? Number(entry.item.UnlockAfterValue) : null;
                    const unit = value ? Number(entry.item.UnlockAfterUnit) || 1 : null;
                    if (original.value !== value || original.unit !== unit) {
                        changes.push(`${entry.item.Title || original.title}: ${original.value ? `${original.value} ${unlockUnitLabel(original.unit, original.value)}` : "immediate"} to ${value ? `${value} ${unlockUnitLabel(unit, value)}` : "immediate"}`);
                    }
                });
        });
        return changes;
    }

    function unlockUnitLabel(unit, value) {
        const label = Number(unit) === 3 ? "month" : Number(unit) === 2 ? "week" : "day";
        return Number(value) === 1 ? label : `${label}s`;
    }

    function findUnselectedExam(exemptId) {
        for (const section of state.Sections) {
            const exam = section.Exams.find(function (item) { return !item.ExamId && Number(item.Id) !== Number(exemptId); });
            if (exam) return exam;
        }
        return null;
    }

    function serializeForSubmit() {
        normalizeOrders();
        const submissionState = JSON.parse(JSON.stringify(state));
        if (createExamPlaceholderId !== null) {
            submissionState.Sections.forEach(function (section) {
                section.Exams = section.Exams.filter(function (exam) { return Number(exam.Id) !== Number(createExamPlaceholderId); });
            });
        }
        jsonInput.value = JSON.stringify(submissionState);
    }

    document.querySelector('[data-course-add-top="section"]')?.addEventListener("click", addSection);
    form.addEventListener("submit", function (event) {
        const invalidExam = findUnselectedExam(createExamPlaceholderId);
        if (invalidExam) {
            event.preventDefault();
            selected = { type: "exam", id: Number(invalidExam.Id) };
            render();
            main.querySelector('[data-editor-field="ExamId"]')?.focus();
            return;
        }

        const unlockChanges = changedUnlockRules();
        if (!allowConfirmedSubmit && unlockChanges.length) {
            event.preventDefault();
            const dialog = document.getElementById("courseUnlockConfirmDialog");
            const list = dialog?.querySelector("[data-course-unlock-change-list]");
            if (list) list.innerHTML = unlockChanges.map(function (change) { return `<li>${escapeHtml(change)}</li>`; }).join("");
            dialog?.showModal();
            return;
        }

        destroyRichEditor();
        serializeForSubmit();
    });

    document.querySelector("[data-course-unlock-confirm]")?.addEventListener("click", function () {
        document.getElementById("courseUnlockConfirmDialog")?.close();
        allowConfirmedSubmit = true;
        form.requestSubmit();
    });
    document.querySelector("[data-course-unlock-cancel]")?.addEventListener("click", function () {
        document.getElementById("courseUnlockConfirmDialog")?.close();
        form.action = defaultFormAction;
        createExamPlaceholderId = null;
    });
    document.getElementById("courseUnlockConfirmDialog")?.addEventListener("cancel", function () {
        form.action = defaultFormAction;
        createExamPlaceholderId = null;
    });
    render();
}());
