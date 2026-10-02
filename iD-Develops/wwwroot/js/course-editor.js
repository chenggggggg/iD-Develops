(function () {
    "use strict";

    const dataElement = document.getElementById("courseEditorData");
    const outline = document.querySelector("[data-course-editor-outline]");
    const main = document.querySelector("[data-course-editor-main]");
    const form = document.getElementById("course-edit-form");
    const jsonInput = document.querySelector("[data-course-editor-json]");
    if (!dataElement || !outline || !main || !form || !jsonInput) return;

    const state = JSON.parse(dataElement.textContent || "{}");
    state.Sections = state.Sections || [];
    state.CreditTypes = state.CreditTypes || [];
    state.CreditPolicies = state.CreditPolicies || [];
    state.ExamOptions = state.ExamOptions || [];
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
        return `<button class="tw:group tw:flex tw:min-h-12 tw:w-full tw:items-center tw:gap-3 tw:rounded-md tw:border-0 tw:bg-transparent tw:px-2.5 tw:py-2 tw:text-left tw:text-slate-700 tw:transition-colors hover:tw:bg-slate-50 hover:tw:text-slate-950 focus-visible:tw:bg-slate-50 focus-visible:tw:outline-2 focus-visible:tw:outline-offset-1 focus-visible:tw:outline-[#b23a48]" type="button" data-add-child="${type}" data-section-id="${sectionId}">
            <span class="tw:grid tw:size-8 tw:shrink-0 tw:place-items-center tw:rounded-md tw:bg-slate-100 tw:text-sm tw:text-slate-600 group-hover:tw:bg-white group-hover:tw:text-[#b23a48]" aria-hidden="true"><i class="fa-solid ${icon}"></i></span>
            <span class="tw:min-w-0 tw:flex-1">
                <span class="tw:block tw:text-sm tw:font-semibold tw:leading-5">${label}</span>
                <span class="tw:block tw:text-xs tw:leading-4 tw:text-slate-500">${description}</span>
            </span>
            <i class="fa-solid fa-chevron-right tw:text-[0.65rem] tw:text-slate-400" aria-hidden="true"></i>
        </button>`;
    }

    function addContentMenu(section) {
        const menuId = `course-add-content-${section.Id}`;
        return `<div class="tw:relative tw:px-1 tw:pb-1 tw:pt-2" data-add-content-root>
            <button class="tw:flex tw:min-h-10 tw:w-full tw:items-center tw:gap-2 tw:rounded-md tw:border tw:border-dashed tw:border-slate-300 tw:bg-white tw:px-3 tw:py-2 tw:text-sm tw:font-semibold tw:text-slate-600 tw:transition-colors hover:tw:border-[#b23a48] hover:tw:bg-rose-50/50 hover:tw:text-[#b23a48] focus-visible:tw:outline-2 focus-visible:tw:outline-offset-2 focus-visible:tw:outline-[#b23a48]" type="button" data-add-content-toggle aria-controls="${menuId}" aria-expanded="false">
                <i class="fa-solid fa-plus tw:text-xs" aria-hidden="true"></i>
                <span class="tw:flex-1 tw:text-left">Add content</span>
                <i class="fa-solid fa-chevron-down tw:text-[0.65rem] tw:transition-transform" data-add-content-chevron aria-hidden="true"></i>
            </button>
            <div class="tw:mt-2 tw:grid tw:gap-0.5 tw:rounded-lg tw:border tw:border-slate-200 tw:bg-white tw:p-1.5 tw:shadow-lg" id="${menuId}" data-add-content-menu hidden>
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
            ? `<span class="course-editor-inline-field"><span class="tw:block tw:min-w-0 tw:truncate tw:px-2 tw:text-sm tw:font-medium tw:text-slate-800">${escapeHtml(item.Title)}</span></span>`
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

    function renderCourse() {
        return `<section class="course-editor-heading"><p class="course-kicker"><i class="fa-solid fa-book-open" aria-hidden="true"></i> Course</p><div class="course-editor-title-field"><input type="text" value="${escapeHtml(state.Name)}" maxlength="200" data-editor-field="Name" placeholder="Course name" /><i class="fa-solid fa-pencil" aria-hidden="true"></i></div></section>
            <section class="course-editor-empty-preview"><i class="fa-solid fa-layer-group" aria-hidden="true"></i><strong>Course structure</strong></section>`;
    }

    function renderSection(section) {
        return `${heading("Section", null, section, "Untitled section")}
            <section class="course-editor-section-preview"><strong>${escapeHtml(section.Title || "Untitled section")}</strong><span>${section.Lectures.length} lectures &middot; ${section.Assignments.length} assignments &middot; ${section.Classes.length} classes &middot; ${section.Exams.length} exams</span></section>
            <section class="course-editor-fields">${unlockField(section)}</section>`;
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
            </section>`;
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
            </section>`;
    }

    function renderExam(exam) {
        const examOptions = state.ExamOptions.map(function (option) {
            const status = Number(option.PublishStatus) === 1 ? "Published" : "Draft";
            return { value: option.Id, label: `${option.Name} (${status})` };
        });
        const status = Number(exam.PublishStatus) === 1 ? "Published" : Number(exam.PublishStatus) === 2 ? "Archived" : "Draft";
        return `${heading("Exam", "fa-file-circle-check", exam, "Exam")}
            <div class="tw:mb-5 tw:rounded-xl tw:border ${Number(exam.PublishStatus) === 1 ? "tw:border-emerald-200 tw:bg-emerald-50 tw:text-emerald-900" : "tw:border-amber-200 tw:bg-amber-50 tw:text-amber-900"} tw:px-4 tw:py-3 tw:text-sm">
                <strong>${status}</strong>${Number(exam.PublishStatus) === 1 ? " · visible when this content unlocks" : " · not visible to students until published"}
            </div>
            <section class="course-editor-fields">
                ${selectField("Exam", "ExamId", exam.ExamId, examOptions, "Select an exam")}
                ${unlockField(exam)}
                ${checkboxField("Required for completion", "IsRequiredForCompletion", exam.IsRequiredForCompletion !== false)}
                ${field("Minimum passing score", "MinimumPassingScore", exam.MinimumPassingScore ?? 0, { type: "number", min: 0, max: 100000 })}
                ${selectField("When the learner does not pass", "FailureAction", exam.FailureAction || 2, [{ value: 1, label: "Allow course progress" }, { value: 2, label: "Require a passing score" }])}
            </section>
            <div class="tw:mt-5 tw:flex tw:flex-wrap tw:gap-2">
                <a class="portal-btn portal-btn-outline portal-btn-sm" href="/portal/examination/edit?examId=${Number(exam.ExamId)}"><i class="fa-solid fa-pen" aria-hidden="true"></i>Edit exam</a>
                <button class="portal-btn portal-btn-outline portal-btn-sm" type="button" data-create-new-exam data-section-id="${findTarget(selected).parent?.Id || ""}"><i class="fa-solid fa-plus" aria-hidden="true"></i>Create and attach new exam</button>
            </div>`;
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
            ? `<a href="/Portal/Schedule/Roster?courseClassId=${Number(courseClass.Id)}&mode=single#schedule-session" class="tw:inline-flex tw:items-center tw:justify-center tw:gap-2 tw:rounded-lg tw:bg-sky-700 tw:px-4 tw:py-2.5 tw:text-sm tw:font-semibold tw:text-white tw:transition hover:tw:bg-sky-800"><i class="fa-solid fa-calendar-plus" aria-hidden="true"></i>${upcomingSessionCount === 0 ? "Add session" : "Manage sessions"}</a>`
            : `<span class="tw:text-xs tw:text-slate-500">Save the course before adding sessions.</span>`;
        return `${heading("Class", "fa-calendar-days", courseClass, "Untitled class")}
            <section class="course-editor-class-preview"><i class="fa-solid fa-calendar-days" aria-hidden="true"></i><strong>${escapeHtml(courseClass.Title || "Untitled class")}</strong><span>${Number(courseClass.DurationMinutes) || 0} min · ${format === 2 ? "One-to-one" : "Group"}</span></section>
            <div class="tw:grid tw:gap-4 tw:py-4">
                <details class="tw:group tw:rounded-2xl tw:border tw:border-slate-200 tw:bg-white tw:shadow-sm" open>
                    <summary class="tw:flex tw:cursor-pointer tw:list-none tw:items-center tw:justify-between tw:gap-4 tw:px-5 tw:py-4"><span><strong class="tw:block tw:text-sm tw:text-slate-900">Class details</strong><small class="tw:mt-1 tw:block tw:text-xs tw:text-slate-500">The format, duration, and capacity of each session.</small></span><i class="fa-solid fa-chevron-down tw:text-slate-400 tw:transition-transform tw:group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields tw:!border-b-0 tw:border-t tw:border-slate-100 tw:!px-5 tw:!py-5">
                        ${selectField("Session type", "Format", format, [{ value: 1, label: "Group session" }, { value: 2, label: "One-to-one session" }])}
                        ${field("Duration", "DurationMinutes", courseClass.DurationMinutes || 60, { type: "number", min: 5, max: 1440, placeholder: "Minutes" })}
                        ${format === 1 ? field("Maximum learners per session", "Capacity", courseClass.Capacity || 1, { type: "number", min: 1, max: 10000, placeholder: "Seats" }) : ""}
                    </section>
                </details>
                <details class="tw:group tw:rounded-2xl tw:border tw:border-slate-200 tw:bg-white tw:shadow-sm">
                    <summary class="tw:flex tw:cursor-pointer tw:list-none tw:items-center tw:justify-between tw:gap-4 tw:px-5 tw:py-4"><span><strong class="tw:block tw:text-sm tw:text-slate-900">Course progress</strong><small class="tw:mt-1 tw:block tw:text-xs tw:text-slate-500">Choose when this class unlocks and whether attendance is required.</small></span><i class="fa-solid fa-chevron-down tw:text-slate-400 tw:transition-transform tw:group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields tw:!border-b-0 tw:border-t tw:border-slate-100 tw:!px-5 tw:!py-5">
                        ${unlockField(courseClass)}
                        ${checkboxField("Required for course completion", "IsRequiredForCompletion", Boolean(courseClass.IsRequiredForCompletion))}
                        <p class="tw:col-span-full tw:m-0 tw:text-xs tw:leading-5 tw:text-slate-500">Required classes are completed automatically after the learner is marked attended. No-shows do not count.</p>
                    </section>
                </details>
                <details class="tw:group tw:rounded-2xl tw:border tw:border-slate-200 tw:bg-white tw:shadow-sm">
                    <summary class="tw:flex tw:cursor-pointer tw:list-none tw:items-center tw:justify-between tw:gap-4 tw:px-5 tw:py-4"><span><strong class="tw:block tw:text-sm tw:text-slate-900">Booking</strong><small class="tw:mt-1 tw:block tw:text-xs tw:text-slate-500">Control who can book and what entitlement they need.</small></span><i class="fa-solid fa-chevron-down tw:text-slate-400 tw:transition-transform tw:group-open:rotate-180" aria-hidden="true"></i></summary>
                    <section class="course-editor-fields tw:!border-b-0 tw:border-t tw:border-slate-100 tw:!px-5 tw:!py-5">
                        ${selectField("Who can book?", "BookingAccess", bookingAccess, [{ value: 1, label: "Enrolled learners · included with course" }, { value: 2, label: "Anyone with the required Credit Product" }, { value: 3, label: "Enrolled learners · Credit Product required" }])}
                        ${selectField("Booking opens", "BookingEligibility", bookingEligibility, [{ value: 1, label: "When this class unlocks" }, { value: 2, label: "When the previous section unlocks" }])}
                        ${checkboxField("Show in student self-booking", "IsVisibleForStudentBooking", courseClass.IsVisibleForStudentBooking !== false)}
                        ${!usesCredit ? field("Sessions included per learner", "EnrollmentBookingLimit", courseClass.EnrollmentBookingLimit || "", { type: "number", min: 1, max: 100000, placeholder: "Unlimited" }) : ""}
                        ${usesCredit ? selectField("Required Credit Product", "RequiredCreditProductId", courseClass.RequiredCreditProductId, creditProducts, "Select Credit Product") : ""}
                        ${usesCredit ? field("Credits per booking", "CreditCost", courseClass.CreditCost || 1, { type: "number", min: 1, max: 100000 }) : ""}
                        ${usesCredit ? '<p class="tw:col-span-full tw:m-0 tw:text-xs tw:leading-5 tw:text-slate-500">Attendance, no-show, and cancellation rules come from the selected Credit Product.</p>' : ""}
                    </section>
                </details>
                <section class="tw:flex tw:flex-col tw:gap-4 tw:rounded-2xl tw:border tw:border-slate-200 tw:bg-slate-50 tw:p-5 tw:sm:flex-row tw:sm:items-center tw:sm:justify-between">
                    <span><strong class="tw:block tw:text-sm tw:text-slate-900">${sessionStatus}</strong><small class="tw:mt-1 tw:block tw:text-xs tw:text-slate-500">${sessionHelp}</small></span>
                    ${sessionAction}
                </section>
            </div>`;
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
                    candidate.closest("[data-add-content-root]")?.querySelector("[data-add-content-chevron]")?.classList.remove("tw:rotate-180");
                });
                menu.hidden = !shouldOpen;
                button.setAttribute("aria-expanded", String(shouldOpen));
                root.querySelector("[data-add-content-chevron]")?.classList.toggle("tw:rotate-180", shouldOpen);
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
    }

    function startCreateExam(section) {
        normalizeOrders();
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
        if (type === "exam" && !state.ExamOptions.length) {
            if (window.confirm("You do not have an active exam to select. Save the course and create one now?")) startCreateExam(section);
            return;
        }
        const item = type === "lecture"
            ? { Id: nextTemporaryId--, Title: "Untitled lecture", OrderNumber: mixedChildren(section).length, Description: "", ContentType: 0, VideoReference: null, UnlockAfterValue: null, UnlockAfterUnit: 1, SourceFiles: [] }
            : type === "assignment"
                ? { Id: nextTemporaryId--, Title: "Untitled assignment", OrderNumber: mixedChildren(section).length, Description: "", EstimatedDurationMinutes: null, Instructions: "", InstructionalVideoReference: null, SupportingFiles: [] }
                : type === "exam"
                    ? { Id: nextTemporaryId--, ExamId: state.ExamOptions[0].Id, Title: state.ExamOptions[0].Name, PublishStatus: state.ExamOptions[0].PublishStatus, OrderNumber: mixedChildren(section).length, UnlockAfterValue: null, UnlockAfterUnit: 1, IsRequiredForCompletion: true, MinimumPassingScore: 0, FailureAction: 2 }
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

    document.querySelector('[data-course-add-top="section"]')?.addEventListener("click", addSection);
    form.addEventListener("submit", function () { destroyRichEditor(); normalizeOrders(); jsonInput.value = JSON.stringify(state); });
    render();
}());
