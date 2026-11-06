(function appointmentBookingPage() {
    "use strict";
    const root = document.querySelector("[data-appointment-booking]");
    if (!root) return;
    const options = JSON.parse(root.querySelector("[data-appointment-options]").textContent || "[]");
    const typeSelect = root.querySelector("[data-appointment-type]");
    const teacherSelect = root.querySelector("[data-appointment-teacher]");
    const dateInput = root.querySelector("[data-appointment-start-date]");
    const slotsRoot = root.querySelector("[data-appointment-slots]");
    const detail = root.querySelector("[data-appointment-detail]");
    const form = root.querySelector("[data-appointment-book-form]");
    let sequence = 0;
    let controller = null;
    let confirming = false;
    let submitting = false;
    const format = settings => new Intl.DateTimeFormat(undefined, { ...settings, timeZone: "Europe/Amsterdam" });
    function zoneLabel(date) {
        const offset = new Intl.DateTimeFormat("en", { timeZone: "Europe/Amsterdam", timeZoneName: "longOffset" }).formatToParts(date).find(part => part.type === "timeZoneName")?.value;
        return offset === "GMT+02:00" ? "CEST" : "CET";
    }
    const parts = Object.fromEntries(new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Amsterdam", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date()).filter(part => part.type !== "literal").map(part => [part.type, part.value]));
    if (!dateInput.value) dateInput.value = `${parts.year}-${parts.month}-${parts.day}`;
    dateInput.min = `${parts.year}-${parts.month}-${parts.day}`;
    const selectedType = () => options.find(item => String(item.id) === typeSelect.value);
    const enoughCredits = item => !item.requiresCredits || item.availableCreditQuantity >= item.creditCost;
    function message(text, busy = false) {
        slotsRoot.setAttribute("aria-busy", String(busy));
        const row = document.createElement("p");
        row.className = "m-0 inline-flex items-center gap-2 text-sm text-slate-600";
        if (busy) {
            const spinner = document.createElement("span");
            spinner.className = "size-4 shrink-0 rounded-full border-2 border-current border-r-transparent motion-safe:animate-spin";
            spinner.setAttribute("aria-hidden", "true");
            row.append(spinner);
        }
        row.append(document.createTextNode(text));
        slotsRoot.replaceChildren(row);
    }
    function updateType(restoreTeacher = "") {
        const item = selectedType();
        teacherSelect.replaceChildren(new Option(item ? "Select a teacher" : "Select an event type first", ""));
        teacherSelect.disabled = !item;
        detail.hidden = !item;
        if (item) {
            root.querySelector("[data-detail-name]").textContent = item.name;
            root.querySelector("[data-detail-duration]").textContent = `${item.durationMinutes} minutes`;
            root.querySelector("[data-detail-credit]").textContent = item.requiresCredits ? `${item.creditCost} ${item.creditLabel}` : "No credits required";
            root.querySelector("[data-detail-delivery]").textContent = ({1: "Zoom", 2: "In person", 3: "Other online", Zoom: "Zoom", InPerson: "In person", OtherOnline: "Other online"})[item.deliveryType] || "Zoom";
            root.querySelector("[data-detail-location]").textContent = item.location || "";
            root.querySelector("[data-detail-location]").hidden = !item.location;
            const balance = root.querySelector("[data-detail-balance]");
            balance.hidden = !item.requiresCredits;
            balance.textContent = enoughCredits(item) ? `Balance: ${item.availableCreditQuantity}` : `Insufficient balance: ${item.availableCreditQuantity}`;
            balance.className = enoughCredits(item) ? "text-sm text-slate-600" : "text-sm font-medium text-red-700";
            item.teachers.forEach(teacher => teacherSelect.append(new Option(teacher.name, teacher.id)));
            if (item.teachers.some(teacher => teacher.id === restoreTeacher)) teacherSelect.value = restoreTeacher;
        }
        loadSlots();
    }
    async function loadSlots() {
        const version = ++sequence;
        controller?.abort();
        controller = new AbortController();
        const item = selectedType();
        const teacherId = teacherSelect.value;
        if (!item || !teacherId || !dateInput.value) { message("Choose an event type, teacher, and date to see available times."); return; }
        message("Checking availability...", true);
        const query = new URLSearchParams({ handler: "Slots", appointmentTypeId: String(item.id), teacherUserId: teacherId, startDate: dateInput.value, days: "14", attendeeUserId: root.dataset.attendeeId });
        try {
            const response = await fetch(`${location.pathname}?${query}`, { cache: "no-store", signal: controller.signal, headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!response.ok) throw new Error("Availability could not be loaded. Please try again.");
            const result = await response.json();
            if (version !== sequence) return;
            const slots = Array.isArray(result) ? result : result.$values;
            if (!Array.isArray(slots)) throw new Error("Availability could not be loaded. Please try again.");
            renderSlots(slots, item, teacherId);
        } catch (error) {
            if (error.name !== "AbortError" && version === sequence) message(error.message);
        }
    }
    function renderSlots(slots, item, teacherId) {
        slotsRoot.replaceChildren();
        slotsRoot.setAttribute("aria-busy", "false");
        if (!slots.length) { message("No availability in this period. Try another date or teacher."); return; }
        const groups = new Map();
        slots.forEach(slot => {
            const start = new Date(slot.startUtc);
            const key = format({year: "numeric", month: "2-digit", day: "2-digit"}).format(start);
            if (!groups.has(key)) groups.set(key, { start, slots: [] });
            groups.get(key).slots.push(slot);
        });
        groups.forEach(group => {
            const section = document.createElement("section");
            section.className = "grid gap-3";
            const heading = document.createElement("h3");
            heading.className = "m-0 text-sm font-semibold text-slate-800";
            heading.textContent = format({weekday: "long", day: "numeric", month: "long"}).format(group.start);
            const grid = document.createElement("div");
            grid.className = "grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-4";
            group.slots.forEach(slot => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "min-h-11 rounded-lg border border-[var(--id-blue)] bg-white px-3 py-2 text-sm font-semibold text-[var(--id-blue)] hover:bg-[var(--id-blue-soft)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--id-blue)] disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50 disabled:text-slate-400 disabled:line-through";
                const start = new Date(slot.startUtc);
                button.textContent = `${format({hour: "2-digit", minute: "2-digit"}).format(start)} ${zoneLabel(start)}`;
                button.disabled = !slot.isAvailable || !enoughCredits(item);
                button.title = slot.unavailableReason || (button.disabled ? "Not enough credits" : "Book this time");
                button.dataset.slotStart = slot.startUtc;
                if (!button.disabled) button.addEventListener("click", () => book(slot, item, teacherId, button));
                grid.append(button);
            });
            section.append(heading, grid);
            slotsRoot.append(section);
        });
    }
    async function book(slot, item, teacherId, button) {
        if (confirming || submitting || selectedType()?.id !== item.id || teacherSelect.value !== teacherId) return;
        confirming = true;
        try {
            const when = `${format({dateStyle: "full", timeStyle: "short"}).format(new Date(slot.startUtc))} ${zoneLabel(new Date(slot.startUtc))}`;
            const confirmed = await window.appConfirm({
                title: "Confirm booking",
                message: `Book ${item.name} for ${root.dataset.attendeeName} on ${when}? ${item.requiresCredits ? `This reserves ${item.creditCost} ${item.creditLabel} from this attendee's balance.` : "No credits are required."}`,
                confirmLabel: "Book session", tone: "info"
            });
            if (!confirmed || selectedType()?.id !== item.id || teacherSelect.value !== teacherId) return;
            submitting = true;
            slotsRoot.querySelectorAll("button").forEach(item => { item.disabled = true; });
            button.setAttribute("aria-busy", "true");
            const spinner = document.createElement("span");
            spinner.className = "inline-block size-4 rounded-full border-2 border-current border-r-transparent motion-safe:animate-spin";
            spinner.setAttribute("aria-hidden", "true");
            button.replaceChildren(spinner, document.createTextNode(" Booking..."));
            root.querySelector("[data-book-type]").value = item.id;
            root.querySelector("[data-book-teacher]").value = teacherId;
            root.querySelector("[data-book-start]").value = slot.startUtc;
            form.requestSubmit();
        } finally { confirming = false; }
    }
    root.querySelectorAll("[data-cancel-session]").forEach(cancelForm => {
        cancelForm.addEventListener("submit", async event => {
            if (cancelForm.dataset.confirmed === "true") return;
            event.preventDefault();
            if (cancelForm.dataset.confirming === "true") return;
            cancelForm.dataset.confirming = "true";
            try {
                if (await window.appConfirm({title: "Cancel session?", message: cancelForm.dataset.cancelMessage, confirmLabel: "Cancel session", tone: "warning"})) {
                    cancelForm.dataset.confirmed = "true";
                    cancelForm.requestSubmit();
                }
            } finally { delete cancelForm.dataset.confirming; }
        });
        cancelForm.querySelector('button[type="submit"]').disabled = false;
    });
    typeSelect.addEventListener("change", () => updateType());
    teacherSelect.addEventListener("change", loadSlots);
    dateInput.addEventListener("change", loadSlots);
    dateInput.addEventListener("input", loadSlots);
    if (options.some(item => String(item.id) === root.dataset.selectedType)) {
        typeSelect.value = root.dataset.selectedType;
        updateType(root.dataset.selectedTeacher || "");
    }
    typeSelect.disabled = false;
})();
