(function () {
    "use strict";
    const root = document.querySelector("[data-appointment-booking]");
    if (!root) return;
    const options = JSON.parse(root.querySelector("[data-appointment-options]").textContent || "[]");
    const typeSelect = root.querySelector("[data-appointment-type]");
    const teacherSelect = root.querySelector("[data-appointment-teacher]");
    const dateInput = root.querySelector("[data-appointment-start-date]");
    const slotsRoot = root.querySelector("[data-appointment-slots]");
    const detail = root.querySelector("[data-appointment-detail]");
    const bookingForm = root.querySelector("[data-appointment-book-form]");
    const timeZoneLabel = root.querySelector("[data-appointment-time-zone]");

    function selectedTimeZone() {
        return "Europe/Amsterdam";
    }

    function formatter(options) {
        return new Intl.DateTimeFormat(undefined, Object.assign({}, options, { timeZone: selectedTimeZone() }));
    }

    timeZoneLabel.textContent = "Times shown in Amsterdam time";

    const today = new Date();
    const amsterdamDateParts = {};
    new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Amsterdam", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(today).forEach(function (part) {
        if (part.type !== "literal") amsterdamDateParts[part.type] = part.value;
    });
    dateInput.value = `${amsterdamDateParts.year}-${amsterdamDateParts.month}-${amsterdamDateParts.day}`;
    dateInput.min = dateInput.value;

    function selectedType() { return options.find(item => String(item.id) === typeSelect.value); }

    function updateType() {
        const item = selectedType();
        teacherSelect.replaceChildren();
        if (!item) {
            teacherSelect.append(new Option("Select a session first", ""));
            teacherSelect.disabled = true;
            detail.hidden = true;
            renderMessage("Select a session and teacher to see available times.");
            return;
        }
        detail.hidden = false;
        root.querySelector("[data-detail-name]").textContent = item.name;
        root.querySelector("[data-detail-duration]").textContent = `${item.durationMinutes} minutes`;
        root.querySelector("[data-detail-credit]").textContent = `${item.creditCost} ${item.creditLabel}`;
        const enough = item.availableCreditQuantity >= item.creditCost;
        const balance = root.querySelector("[data-detail-balance]");
        balance.textContent = enough ? `Balance: ${item.availableCreditQuantity}` : `Insufficient balance: ${item.availableCreditQuantity}`;
        balance.classList.toggle("is-error", !enough);
        teacherSelect.append(new Option("Select a teacher", ""));
        item.teachers.forEach(teacher => teacherSelect.append(new Option(`${teacher.name} (${teacher.email})`, teacher.id)));
        teacherSelect.disabled = false;
        loadSlots();
    }

    async function loadSlots() {
        const item = selectedType();
        if (!item || !teacherSelect.value) { renderMessage("Select a teacher to see available times."); return; }
        renderMessage("Checking availability…");
        const query = new URLSearchParams({ handler: "Slots", appointmentTypeId: item.id, teacherUserId: teacherSelect.value, startDate: dateInput.value, days: "14", _: Date.now() });
        try {
            const response = await fetch(`${window.location.pathname}?${query}`, { cache: "no-store", headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!response.ok) throw new Error("Availability could not be loaded.");
            renderSlots(await response.json(), item);
        } catch (error) { renderMessage(error.message); }
    }

    function renderSlots(slots, item) {
        slotsRoot.replaceChildren();
        if (!slots.length) { renderMessage("This teacher has no availability in the selected period."); return; }
        const groups = new Map();
        slots.forEach(slot => {
            const start = new Date(slot.startUtc);
            const key = formatter({ year: "numeric", month: "2-digit", day: "2-digit" }).format(start);
            if (!groups.has(key)) groups.set(key, { start, slots: [] });
            groups.get(key).slots.push(slot);
        });
        groups.forEach(group => {
            const section = document.createElement("section");
            section.className = "appointment-slot-day";
            const heading = document.createElement("h3");
            heading.textContent = formatter({ weekday: "long", day: "numeric", month: "long" }).format(group.start);
            const grid = document.createElement("div");
            grid.className = "appointment-slot-grid";
            group.slots.forEach(slot => {
                const button = document.createElement("button");
                button.type = "button";
                button.disabled = !slot.isAvailable || item.availableCreditQuantity < item.creditCost;
                button.textContent = formatter({ hour: "2-digit", minute: "2-digit" }).format(new Date(slot.startUtc));
                button.title = slot.unavailableReason || (button.disabled ? "Not enough credits" : "Book this time");
                if (slot.isAvailable && item.availableCreditQuantity >= item.creditCost) button.addEventListener("click", () => book(slot));
                grid.append(button);
            });
            section.append(heading, grid);
            slotsRoot.append(section);
        });
    }

    function book(slot) {
        const item = selectedType();
        const when = formatter({ dateStyle: "full", timeStyle: "short" }).format(new Date(slot.startUtc));
        if (!window.confirm(`Book ${item.name} for ${when}? This uses ${item.creditCost} ${item.creditLabel}.`)) return;
        root.querySelector("[data-book-type]").value = item.id;
        root.querySelector("[data-book-teacher]").value = teacherSelect.value;
        root.querySelector("[data-book-start]").value = slot.startUtc;
        bookingForm.submit();
    }

    function renderMessage(message) { slotsRoot.replaceChildren(Object.assign(document.createElement("p"), { className: "appointment-empty", textContent: message })); }
    typeSelect.addEventListener("change", updateType);
    teacherSelect.addEventListener("change", loadSlots);
    dateInput.addEventListener("change", loadSlots);
}());
