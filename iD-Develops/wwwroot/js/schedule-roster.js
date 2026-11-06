(function () {
    "use strict";

    const root = document.querySelector("[data-roster-root]");
    if (!root) return;

    let availability = [];
    try {
        availability = JSON.parse(root.querySelector("[data-availability-json]")?.textContent || "[]");
    } catch (_error) {
        availability = [];
    }

    function teacherAvailability(teacherId) {
        return availability.filter(function (item) { return item.TeacherUserId === teacherId; });
    }

    function toMinutes(value) {
        const parts = (value || "").split(":").map(Number);
        return parts.length === 2 ? parts[0] * 60 + parts[1] : 0;
    }

    function fitsWindow(window, dayOfWeek, startMinutes, durationMinutes) {
        if (window.DayOfWeek !== dayOfWeek) return false;
        return startMinutes >= toMinutes(window.Start) &&
            startMinutes + durationMinutes <= toMinutes(window.End);
    }

    function filterTeacherOptions(select, predicate, preserveSelection = false) {
        if (!select || select.tagName !== "SELECT") return 0;
        let availableCount = 0;
        Array.from(select.options).forEach(function (option, index) {
            if (index === 0) return;
            const available = predicate(option.value);
            const keepSelected = preserveSelection && option.selected;
            option.hidden = !available && !keepSelected;
            option.disabled = !available && !keepSelected;
            if (available) availableCount += 1;
        });
        if (select.value && select.selectedOptions[0]?.disabled) select.value = "";
        return availableCount;
    }

    const availabilityRoot = root.querySelector("[data-availability-form-root]");
    const availabilityTeacher = availabilityRoot?.querySelector("[data-availability-teacher]");
    const availabilityZone = availabilityRoot?.querySelector("[data-availability-time-zone]");
    const availabilityDays = Array.from(availabilityRoot?.querySelectorAll("[data-availability-day]") || []);

    function updateAvailabilityDay(row) {
        const enabled = row.querySelector("[data-availability-enabled]")?.checked === true;
        row.dataset.available = String(enabled);
        const status = row.querySelector("[data-availability-status]");
        if (status) status.textContent = enabled ? "Available" : "Unavailable";
        row.querySelectorAll("[data-availability-start], [data-availability-end]").forEach(function (input) {
            input.disabled = !enabled;
        });
    }

    function loadAvailabilityTeacher() {
        if (!availabilityTeacher) return;
        const rows = teacherAvailability(availabilityTeacher.value);
        if (rows.length && availabilityZone) availabilityZone.value = rows[0].TimeZoneId;
        availabilityDays.forEach(function (dayRow) {
            const saved = rows.find(function (item) { return item.DayOfWeek === dayRow.dataset.availabilityDay; });
            const checkbox = dayRow.querySelector("[data-availability-enabled]");
            const start = dayRow.querySelector("[data-availability-start]");
            const end = dayRow.querySelector("[data-availability-end]");
            if (checkbox) checkbox.checked = Boolean(saved);
            if (start) start.value = saved?.Start || "09:00";
            if (end) end.value = saved?.End || "17:00";
            updateAvailabilityDay(dayRow);
        });
    }

    availabilityDays.forEach(function (dayRow) {
        dayRow.querySelector("[data-availability-enabled]")?.addEventListener("change", function () {
            updateAvailabilityDay(dayRow);
        });
        updateAvailabilityDay(dayRow);
    });
    availabilityTeacher?.addEventListener("change", loadAvailabilityTeacher);

    const form = root.querySelector("[data-roster-form]");
    const editor = root.querySelector("[data-roster-editor]");
    const title = root.querySelector("[data-roster-editor-title]");
    const submitLabel = root.querySelector("[data-roster-submit-label]");
    const rosterTeacher = form?.querySelector("[data-roster-teacher]");
    const rosterDay = form?.querySelector("[data-roster-day]");
    const rosterStart = form?.querySelector("[data-roster-start]");
    const rosterZone = form?.querySelector("[data-roster-time-zone]");
    const rosterClass = form?.querySelector("[data-roster-class]");
    const rosterDuration = form?.querySelector('[data-roster-field="DurationMinutes"]');
    const rosterCapacity = form?.querySelector('[data-roster-field="Capacity"]');
    const availabilityHint = form?.querySelector("[data-roster-availability-hint]");

    function applyRosterAvailability() {
        if (!form || !rosterTeacher || !rosterDay || !rosterStart || !rosterZone) return;
        const duration = Number(rosterDuration?.value || 60);
        const startMinutes = toMinutes(rosterStart.value);
        const availableCount = filterTeacherOptions(rosterTeacher, function (teacherId) {
            return teacherAvailability(teacherId).some(function (window) {
                return fitsWindow(window, rosterDay.value, startMinutes, duration);
            });
        });
        const selectedWindow = teacherAvailability(rosterTeacher.value).find(function (window) {
            return fitsWindow(window, rosterDay.value, startMinutes, duration);
        });
        if (selectedWindow) rosterZone.value = selectedWindow.TimeZoneId;
        if (availabilityHint) {
            availabilityHint.textContent = rosterTeacher.tagName === "SELECT"
                ? `${availableCount} staff member${availableCount === 1 ? "" : "s"} available for this time and duration.`
                : "Availability is used only when an administrator assigns a teacher.";
        }
    }

    function applyClassDefaults() {
        const option = rosterClass?.selectedOptions[0];
        if (!option?.value) return;
        if (rosterDuration && option.dataset.duration) rosterDuration.value = option.dataset.duration;
        if (rosterCapacity && option.dataset.capacity) rosterCapacity.value = option.dataset.capacity;
        updateClassSummary();
        applyRosterAvailability();
    }

    function updateClassSummary() {
        const summary = form?.querySelector("[data-class-summary]");
        if (summary) summary.textContent = rosterClass?.value
            ? `${rosterDuration.value} minutes · ${rosterCapacity.value} seats`
            : "Duration and capacity follow the selected course class.";
    }

    function focusEditor() {
        editor.scrollIntoView({ behavior: "instant", block: "start" });
        form.querySelector("select, input:not([type=hidden])")?.focus({ preventScroll: true });
    }

    function setField(name, value) {
        const input = form?.querySelector(`[data-roster-field="${name}"]`);
        if (!input) return;
        if (input.type === "checkbox") input.checked = Boolean(value);
        else {
            const normalized = value == null ? "" : String(value);
            if (input.tagName === "SELECT" && normalized && !Array.from(input.options).some(function (option) { return option.value === normalized; })) {
                input.add(new Option(normalized, normalized));
            }
            input.value = normalized;
        }
    }

    function editRule(button) {
        let rule;
        try { rule = JSON.parse(button.dataset.rosterRule); }
        catch (_error) { return; }
        Object.keys(rule).forEach(function (key) { setField(key, rule[key]); });
        title.textContent = "Edit recurring class";
        submitLabel.textContent = "Update recurring class";
        updateClassSummary();
        syncDelivery(form);
        form.querySelector("[data-schedule-advanced]").open = true;
        applyRosterAvailability();
        focusEditor();
    }

    function resetForm() {
        form.reset();
        setField("Id", 0);
        title.textContent = "New recurring class";
        submitLabel.textContent = "Save recurring class";
        syncDelivery(form);
        form.querySelector("[data-schedule-advanced]").open = false;
        applyClassDefaults();
        applyRosterAvailability();
    }

    root.querySelectorAll("[data-roster-edit]").forEach(function (button) {
        button.addEventListener("click", function () { editRule(button); });
    });
    root.querySelector("[data-roster-new]")?.addEventListener("click", function () {
        resetForm();
        focusEditor();
    });
    root.querySelector("[data-roster-reset]")?.addEventListener("click", resetForm);
    rosterTeacher?.addEventListener("change", applyRosterAvailability);
    rosterDay?.addEventListener("change", applyRosterAvailability);
    rosterStart?.addEventListener("input", applyRosterAvailability);
    rosterDuration?.addEventListener("input", applyRosterAvailability);
    rosterClass?.addEventListener("change", applyClassDefaults);
    if (form?.dataset.preserveValues !== "true") applyClassDefaults();
    updateClassSummary();
    applyRosterAvailability();

    const manualCourse = root.querySelector("[data-manual-course]");
    const manualStudent = root.querySelector("[data-manual-student]");
    const studentSearch = root.querySelector("[data-student-search]");
    const manualTeacher = root.querySelector("[data-manual-teacher]");
    const manualStart = root.querySelector("[data-manual-start]");
    const manualDuration = root.querySelector("[data-manual-duration]");
    const manualTimeZone = root.querySelector("[data-manual-time-zone]");
    const manualAvailabilityHint = root.querySelector("[data-manual-availability-hint]");

    let manualAvailabilityRequest = 0;

    async function applyManualTeacherAvailability() {
        if (!manualTeacher || manualTeacher.tagName !== "SELECT" || !manualStart || !manualTimeZone) return;
        const duration = Number(manualDuration?.value || 60);
        if (!manualStart.value || !manualTimeZone.value || duration < 5) return;
        const requestNumber = ++manualAvailabilityRequest;
        const query = new URLSearchParams({
            handler: "AvailableTeachers",
            startLocal: manualStart.value,
            timeZoneId: manualTimeZone.value,
            durationMinutes: String(duration)
        });
        if (manualAvailabilityHint) manualAvailabilityHint.textContent = "Checking availability...";
        try {
            const response = await fetch(`${window.location.pathname}?${query.toString()}`, {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw new Error("Availability could not be loaded.");
            const teachers = await response.json();
            if (requestNumber !== manualAvailabilityRequest) return;
            const availableIds = new Set(teachers.map(function (teacher) { return teacher.id; }));
            const preserve = manualTeacher.closest("form").dataset.preserveValues === "true";
            const selected = manualTeacher.value;
            const availableCount = filterTeacherOptions(manualTeacher, function (teacherId) {
                return availableIds.has(teacherId);
            }, preserve);
            manualTeacher.setCustomValidity(preserve && selected && !availableIds.has(selected)
                ? "This teacher is unavailable at the selected time. Choose another time or teacher." : "");
            if (manualAvailabilityHint) {
                manualAvailabilityHint.textContent = manualTeacher.validationMessage || `${availableCount} staff member${availableCount === 1 ? "" : "s"} available for this time and duration.`;
            }
        } catch (_error) {
            if (requestNumber !== manualAvailabilityRequest) return;
            filterTeacherOptions(manualTeacher, function () { return false; }, manualTeacher.closest("form").dataset.preserveValues === "true");
            manualTeacher.setCustomValidity("Teacher availability could not be confirmed. Change the time to try again.");
            if (manualAvailabilityHint) manualAvailabilityHint.textContent = "Availability could not be loaded.";
        }
    }

    function updateManualChoice(changed) {
        if (!manualCourse || !manualStudent) return;
        const audience = root.querySelector("[data-manual-audience]:checked")?.value || "course";
        const forStudent = audience === "student";
        if (changed?.matches("[data-manual-audience]")) {
            if (forStudent) manualCourse.value = "";
            else manualStudent.value = "";
        }
        manualCourse.disabled = forStudent;
        manualCourse.required = !forStudent;
        manualStudent.disabled = !forStudent;
        manualStudent.required = forStudent;
        if (studentSearch) studentSearch.disabled = !forStudent;
        root.querySelectorAll("[data-manual-target]").forEach(section => {
            section.hidden = section.dataset.manualTarget !== audience;
        });
    }

    studentSearch?.addEventListener("input", function () {
        const query = studentSearch.value.trim().toLowerCase();
        Array.from(manualStudent?.options || []).forEach(function (option, index) {
            option.hidden = index > 0 && query !== "" && !option.textContent.toLowerCase().includes(query);
        });
    });
    manualCourse?.addEventListener("change", function () { updateManualChoice(manualCourse); });
    manualStudent?.addEventListener("change", function () { updateManualChoice(manualStudent); });
    root.querySelectorAll("[data-manual-audience]").forEach(input => input.addEventListener("change", () => updateManualChoice(input)));
    manualStart?.addEventListener("input", applyManualTeacherAvailability);
    manualDuration?.addEventListener("input", applyManualTeacherAvailability);
    manualTimeZone?.addEventListener("change", applyManualTeacherAvailability);
    manualTeacher?.addEventListener("change", applyManualTeacherAvailability);
    applyManualTeacherAvailability();
    updateManualChoice();

    function syncDelivery(targetForm) {
        const delivery = targetForm?.querySelector("[data-delivery]")?.value;
        const locationField = targetForm?.querySelector("[data-location-field]");
        if (locationField) {
            locationField.hidden = delivery !== "InPerson";
            const location = locationField.querySelector("input");
            if (location) location.required = delivery === "InPerson";
        }
        if (delivery === "OtherOnline") {
            const advanced = targetForm.querySelector("[data-schedule-advanced]");
            if (advanced) advanced.open = true;
        }
    }

    root.querySelectorAll("form").forEach(targetForm => {
        syncDelivery(targetForm);
        targetForm.querySelector("[data-delivery]")?.addEventListener("change", () => syncDelivery(targetForm));
        // Also validate collapsed controls when legacy unobtrusive validation sets novalidate.
        targetForm.addEventListener("submit", event => {
            if (!targetForm.checkValidity()) {
                event.preventDefault();
                targetForm.reportValidity();
            }
        });
    });
    function revealDetails(element) {
        let details = element.closest("details");
        while (details) {
            details.open = true;
            details = details.parentElement.closest("details");
        }
    }
    root.addEventListener("invalid", event => revealDetails(event.target), true);
    root.querySelectorAll(".input-validation-error").forEach(revealDetails);
    root.querySelector(".validation-summary-errors[tabindex]")?.focus();
}());
