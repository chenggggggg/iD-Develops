(function () {
    "use strict";

    const root = document.querySelector("[data-schedule-root]");
    const calendarElement = document.getElementById("portalScheduleCalendar");
    if (!root || !calendarElement || !window.FullCalendar) return;

    const canManage = root.dataset.canManage === "true";
    const isStaffCalendar = root.dataset.isStaffCalendar === "true";
    const calendarView = root.dataset.calendarView || "mine";
    const dialog = root.querySelector("[data-schedule-dialog]");
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const toast = root.querySelector("[data-schedule-toast]");
    const displayZoneSelect = root.querySelector("[data-schedule-display-zone]");
    const displayZoneStorageKey = "id-develops.schedule-display-zone";
    let displayTimeZone = readDisplayTimeZone();
    let selectedEvent = null;

    const by = function (name) { return dialog.querySelector(`[data-schedule-${name}]`); };

    function eventClass(category) {
        return `schedule-event-${category || "available"}`;
    }

    const eventPalettes = {
        managed: { background: "#e5f3f7", border: "#176b87", text: "#12546b" },
        booked: { background: "#e7f4ec", border: "#287a55", text: "#235e45" },
        recommended: { background: "#fff3dc", border: "#c47c16", text: "#8a560c" },
        available: { background: "#edf7f8", border: "#3b7d8d", text: "#2e6673" },
        cancelled: { background: "#f7edef", border: "#9f3340", text: "#86404a" }
    };

    function eventPalette(category) {
        return eventPalettes[category] || eventPalettes.available;
    }

    function readDisplayTimeZone() {
        try {
            return window.localStorage.getItem(displayZoneStorageKey) === "Europe/Amsterdam"
                ? "Europe/Amsterdam"
                : "local";
        } catch (_error) {
            return "local";
        }
    }

    function intlTimeZone() {
        return displayTimeZone === "Europe/Amsterdam" ? "Europe/Amsterdam" : undefined;
    }

    class IntlNamedTimeZone {
        constructor(timeZoneName) {
            this.timeZoneName = timeZoneName;
            this.formatter = new Intl.DateTimeFormat("en-CA", {
                timeZone: timeZoneName,
                year: "numeric",
                month: "2-digit",
                day: "2-digit",
                hour: "2-digit",
                minute: "2-digit",
                second: "2-digit",
                hourCycle: "h23"
            });
        }

        timestampToArray(milliseconds) {
            const parts = {};
            this.formatter.formatToParts(new Date(milliseconds)).forEach(function (part) {
                if (part.type !== "literal") parts[part.type] = Number(part.value);
            });
            return [
                parts.year,
                parts.month - 1,
                parts.day,
                parts.hour,
                parts.minute,
                parts.second,
                new Date(milliseconds).getUTCMilliseconds()
            ];
        }

        offsetForArray(array) {
            const utcGuess = Date.UTC(array[0], array[1], array[2], array[3], array[4], array[5], array[6] || 0);
            const zoned = this.timestampToArray(utcGuess);
            const zonedAsUtc = Date.UTC(zoned[0], zoned[1], zoned[2], zoned[3], zoned[4], zoned[5], zoned[6] || 0);
            return (zonedAsUtc - utcGuess) / 60000;
        }
    }

    const intlTimeZonePlugin = FullCalendar.createPlugin({
        name: "portal-intl-time-zone",
        namedTimeZonedImpl: IntlNamedTimeZone
    });

    function loadEvents(info, success, failure) {
        const query = new URLSearchParams({
            handler: "Events",
            start: info.start.toISOString(),
            end: info.end.toISOString(),
            CalendarView: calendarView,
            _: String(Date.now())
        });
        fetch(`${window.location.pathname}?${query.toString()}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            cache: "no-store"
        })
            .then(function (response) {
                if (!response.ok) throw new Error("Schedule could not be loaded.");
                return response.json();
            })
            .then(function (items) {
                success(items.map(function (item) {
                    const palette = eventPalette(item.category);
                    return {
                        id: String(item.id),
                        title: item.title,
                        start: item.start,
                        end: item.end,
                        classNames: [eventClass(item.category)],
                        backgroundColor: palette.background,
                        borderColor: palette.border,
                        textColor: palette.text,
                        extendedProps: item
                    };
                }));
            })
            .catch(failure);
    }

    const calendar = new FullCalendar.Calendar(calendarElement, {
        plugins: [intlTimeZonePlugin],
        initialView: window.matchMedia("(max-width: 47.999rem)").matches ? "listWeek" : "timeGridWeek",
        timeZone: displayTimeZone,
        firstDay: 1,
        nowIndicator: true,
        allDaySlot: false,
        slotMinTime: "07:00:00",
        slotMaxTime: "22:00:00",
        height: "auto",
        expandRows: true,
        editable: canManage,
        eventStartEditable: canManage,
        eventDurationEditable: canManage,
        eventOverlap: false,
        headerToolbar: {
            left: "prev,next today",
            center: "title",
            right: "dayGridMonth,timeGridWeek,timeGridDay,listWeek"
        },
        buttonText: {
            today: "Today",
            month: "Month",
            week: "Week",
            day: "Day",
            list: "List"
        },
        events: loadEvents,
        eventDidMount: function (info) {
            const category = info.event.extendedProps.category || "available";
            const palette = eventPalette(category);
            info.el.style.setProperty("--fc-event-bg-color", palette.background);
            info.el.style.setProperty("--fc-event-border-color", palette.border);
            info.el.style.setProperty("--fc-event-text-color", palette.text);
            info.el.style.backgroundColor = palette.background;
            info.el.style.borderColor = palette.border;
            info.el.style.color = palette.text;
            info.el.querySelectorAll(".fc-event-main, .fc-list-event-title, .fc-list-event-time")
                .forEach(function (element) {
                    element.style.color = palette.text;
                });
            if (category === "cancelled") info.el.style.textDecoration = "line-through";
        },
        eventAllow: function (_dropInfo, draggedEvent) {
            const status = draggedEvent.extendedProps.status;
            return Boolean(draggedEvent.extendedProps.canManage) && Number(status) !== 2 && status !== "Cancelled";
        },
        eventClick: function (info) {
            info.jsEvent.preventDefault();
            openDialog(info.event);
        },
        eventDrop: function (info) {
            moveEvent(info.event, info.revert);
        },
        eventResize: function (info) {
            moveEvent(info.event, info.revert);
        }
    });
    calendar.render();

    function openDialog(event) {
        selectedEvent = event;
        const item = event.extendedProps;
        by("dialog-course").textContent = item.courseName || "Course meeting";
        by("dialog-title").textContent = item.title;
        by("dialog-time").textContent = formatRange(event.start, event.end);
        by("dialog-teacher").textContent = item.teacherName;
        by("dialog-capacity").textContent = `${item.spotsRemaining} of ${item.capacity} spots available`;
        by("dialog-location-row").hidden = !item.location;
        by("dialog-location").textContent = item.location || "";
        by("dialog-message").textContent = item.unavailableReason || (item.isRecommended ? "Recommended for your course timeline." : "");

        const now = Date.now();
        const startsAt = new Date(item.start || event.start).getTime();
        const endsAt = new Date(item.end || event.end || event.start).getTime();
        const isWithinJoinWindow = Number.isFinite(startsAt) && Number.isFinite(endsAt) &&
            now >= startsAt && now <= endsAt + 60 * 60 * 1000;
        const join = by("join");
        const canJoinNow = Boolean(item.canJoin && item.joinUrl && isWithinJoinWindow);
        join.hidden = !canJoinNow;
        join.href = canJoinNow ? item.joinUrl : "";
        by("book").hidden = !item.canBook;
        by("cancel-booking").hidden = !(item.isBooked && item.canCancelBooking);
        by("cancel-event").hidden = !item.canManage || Number(item.status) === 2 || item.status === "Cancelled";

        if (typeof dialog.showModal === "function") dialog.showModal();
        else dialog.setAttribute("open", "");
    }

    function closeDialog() {
        selectedEvent = null;
        if (typeof dialog.close === "function") dialog.close();
        else dialog.removeAttribute("open");
    }

    function formatRange(start, end) {
        if (!start) return "";
        const date = new Intl.DateTimeFormat(undefined, {
            weekday: "long",
            day: "numeric",
            month: "long",
            year: "numeric",
            timeZone: intlTimeZone()
        }).format(start);
        const times = new Intl.DateTimeFormat(undefined, {
            hour: "2-digit",
            minute: "2-digit",
            timeZone: intlTimeZone()
        });
        return `${date}, ${times.format(start)}${end ? ` - ${times.format(end)}` : ""}`;
    }

    async function post(handler, values) {
        const body = new URLSearchParams(Object.assign({ __RequestVerificationToken: token }, values));
        const response = await fetch(`${window.location.pathname}?handler=${encodeURIComponent(handler)}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
                "X-Requested-With": "XMLHttpRequest"
            },
            body: body
        });
        const result = await response.json().catch(function () { return {}; });
        if (!response.ok || result.success === false) throw new Error(result.message || "The schedule could not be updated.");
        return result;
    }

    async function runAction(handler, values) {
        try {
            const result = await post(handler, values);
            closeDialog();
            showToast(result.message || "Schedule updated.", false);
            calendar.refetchEvents();
            refreshOverview().catch(function () {
                showToast("The schedule changed, but its summary could not be refreshed.", true);
            });
        } catch (error) {
            showToast(error.message || "The schedule could not be updated.", true);
        }
    }

    async function moveEvent(event, revert) {
        try {
            await post("MoveEvent", {
                scheduledEventId: event.id,
                startUtc: event.start.toISOString(),
                endUtc: (event.end || event.start).toISOString()
            });
            showToast("Meeting moved.", false);
            calendar.refetchEvents();
            refreshOverview().catch(function () {
                showToast("The meeting moved, but the summary could not be refreshed.", true);
            });
        } catch (error) {
            revert();
            showToast(error.message || "The meeting could not be moved.", true);
        }
    }

    function showToast(message, isError) {
        toast.textContent = message;
        toast.classList.toggle("is-error", isError);
        toast.hidden = false;
        window.clearTimeout(showToast.timeoutId);
        showToast.timeoutId = window.setTimeout(function () { toast.hidden = true; }, 4200);
    }

    async function refreshOverview() {
        const query = new URLSearchParams({
            handler: "Overview",
            CalendarView: calendarView,
            _: String(Date.now())
        });
        const response = await fetch(`${window.location.pathname}?${query.toString()}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            cache: "no-store"
        });
        if (!response.ok) throw new Error("The updated schedule summary could not be loaded.");
        renderOverview(await response.json());
    }

    function renderOverview(overview) {
        const upcomingCount = root.querySelector("[data-schedule-upcoming-count]");
        const bookedCount = root.querySelector("[data-schedule-booked-count]");
        const recommendedCount = root.querySelector("[data-schedule-recommended-count]");
        if (upcomingCount) upcomingCount.textContent = String(overview.upcomingCount ?? 0);
        if (bookedCount) bookedCount.textContent = String(isStaffCalendar ? overview.upcomingCount ?? 0 : overview.bookedCount ?? 0);
        if (recommendedCount) recommendedCount.textContent = String(overview.recommendedCount ?? 0);

        renderOverviewList(
            root.querySelector("[data-schedule-upcoming-list]"),
            overview.upcomingItems || [],
            "No upcoming meetings you participate in.");
        renderOverviewList(
            root.querySelector("[data-schedule-available-list]"),
            overview.availableItems || [],
            "No events are currently available to book.");
    }

    function renderOverviewList(list, items, emptyMessage) {
        if (!list) return;
        list.replaceChildren();
        if (!items.length) {
            const empty = document.createElement("p");
            empty.className = "schedule-empty";
            empty.textContent = emptyMessage;
            list.appendChild(empty);
            return;
        }
        items.forEach(function (item) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "schedule-upcoming-item";
            button.dataset.scheduleEventId = String(item.id);
            button.dataset.scheduleEventStart = item.start;
            button.dataset.scheduleEventEnd = item.end;

            const time = document.createElement("time");
            time.dateTime = item.start;
            const start = new Date(item.start);
            const day = document.createElement("strong");
            day.textContent = new Intl.DateTimeFormat(undefined, { day: "2-digit", timeZone: intlTimeZone() }).format(start);
            const month = document.createElement("span");
            month.textContent = new Intl.DateTimeFormat(undefined, { month: "short", timeZone: intlTimeZone() }).format(start);
            time.append(day, month);

            const copy = document.createElement("span");
            const title = document.createElement("strong");
            title.textContent = item.title;
            const meta = document.createElement("small");
            meta.textContent = `${new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", timeZone: intlTimeZone() }).format(start)} \u00b7 ${item.teacherName}`;
            copy.append(title, meta);
            button.append(time, copy);
            list.appendChild(button);
        });
    }

    root.querySelectorAll("[data-schedule-panel-tab]").forEach(function (tab) {
        tab.addEventListener("click", function () {
            const selectedPanel = tab.dataset.schedulePanelTab;
            root.querySelectorAll("[data-schedule-panel-tab]").forEach(function (candidate) {
                const isSelected = candidate === tab;
                candidate.classList.toggle("is-active", isSelected);
                candidate.setAttribute("aria-selected", String(isSelected));
            });
            root.querySelectorAll("[data-schedule-panel]").forEach(function (panel) {
                panel.hidden = panel.dataset.schedulePanel !== selectedPanel;
            });
        });
    });

    if (displayZoneSelect) {
        displayZoneSelect.value = displayTimeZone;
        displayZoneSelect.addEventListener("change", function () {
            displayTimeZone = displayZoneSelect.value === "Europe/Amsterdam" ? "Europe/Amsterdam" : "local";
            try { window.localStorage.setItem(displayZoneStorageKey, displayTimeZone); } catch (_error) { }
            window.dispatchEvent(new CustomEvent("portal-schedule-time-zone-changed"));
            calendar.setOption("timeZone", displayTimeZone);
            calendar.refetchEvents();
            refreshOverview().catch(function (error) { showToast(error.message, true); });
        });
    }

    by("dialog-close").addEventListener("click", closeDialog);
    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) closeDialog();
    });
    by("book").addEventListener("click", function () {
        if (selectedEvent) runAction("Book", { scheduledEventId: selectedEvent.id });
    });
    by("cancel-booking").addEventListener("click", function () {
        if (selectedEvent && window.confirm("Cancel this booking?")) {
            runAction("CancelBooking", { scheduledEventId: selectedEvent.id });
        }
    });
    by("cancel-event").addEventListener("click", function () {
        if (!selectedEvent) return;
        const reason = window.prompt("Reason for cancelling this meeting:", "Schedule change");
        if (reason !== null) runAction("CancelEvent", { scheduledEventId: selectedEvent.id, reason: reason });
    });

    root.querySelector(".schedule-upcoming")?.addEventListener("click", function (clickEvent) {
        const button = clickEvent.target.closest("[data-schedule-event-id]");
        if (!button) return;
        const start = new Date(button.dataset.scheduleEventStart);
        if (!Number.isNaN(start.getTime())) calendar.gotoDate(start);
        window.setTimeout(function () {
            const event = calendar.getEventById(button.dataset.scheduleEventId);
            if (event) openDialog(event);
        }, 350);
    });
    refreshOverview().catch(function () { });
}());
