(function () {
    "use strict";

    function selectedTimeZone() {
        return "Europe/Amsterdam";
    }

    function formatter(options) {
        return new Intl.DateTimeFormat(undefined, Object.assign({}, options, { timeZone: selectedTimeZone() }));
    }

    function zoneLabel(date) {
        if (!date || Number.isNaN(date.getTime())) return "CET/CEST";
        const offset = new Intl.DateTimeFormat("en", { timeZone: selectedTimeZone(), timeZoneName: "longOffset" })
            .formatToParts(date).find(part => part.type === "timeZoneName")?.value;
        return offset === "GMT+02:00" ? "CEST" : offset === "GMT+01:00" ? "CET" : "Amsterdam";
    }

    function zoneRange(start, end) {
        if (!end || Number.isNaN(end.getTime())) return "CET/CEST";
        const labels = new Set([zoneLabel(start), zoneLabel(end)]);
        // Monthly samples also detect summer time when both endpoints are in winter.
        for (let sample = start.getTime(), count = 0; sample < end.getTime() && count < 13; sample += 30 * 86400000, count++)
            labels.add(zoneLabel(new Date(sample)));
        return labels.size > 1 ? "CET/CEST" : [...labels][0];
    }

    function apply(root) {
        (root || document).querySelectorAll("[data-portal-schedule-time]").forEach(function (element) {
            const start = new Date(element.dataset.start);
            const end = element.dataset.end ? new Date(element.dataset.end) : null;
            if (Number.isNaN(start.getTime())) return;

            switch (element.dataset.portalScheduleTime) {
                case "zone-range":
                    element.textContent = zoneRange(start, end);
                    break;
                case "next":
                    element.textContent = `Next: ${formatter({ day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(start)}`;
                    break;
                case "date": {
                    const day = element.querySelector("[data-time-day]");
                    const month = element.querySelector("[data-time-month]");
                    if (day) day.textContent = formatter({ day: "2-digit" }).format(start);
                    if (month) month.textContent = formatter({ month: "short" }).format(start);
                    break;
                }
                case "range": {
                    const date = formatter({ weekday: "long", hour: "2-digit", minute: "2-digit" }).format(start);
                    const endTime = end && !Number.isNaN(end.getTime())
                        ? formatter({ hour: "2-digit", minute: "2-digit" }).format(end)
                        : "";
                    element.textContent = endTime ? `${date} - ${endTime}` : date;
                    break;
                }
            }
        });
    }

    window.PortalScheduleTime = { apply: apply, zoneLabel: zoneLabel, zoneRange: zoneRange };
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", function () { apply(document); });
    } else {
        apply(document);
    }
}());
