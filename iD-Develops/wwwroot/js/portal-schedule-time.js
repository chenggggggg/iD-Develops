(function () {
    "use strict";

    function selectedTimeZone() {
        return "Europe/Amsterdam";
    }

    function formatter(options) {
        return new Intl.DateTimeFormat(undefined, Object.assign({}, options, { timeZone: selectedTimeZone() }));
    }

    function apply(root) {
        (root || document).querySelectorAll("[data-portal-schedule-time]").forEach(function (element) {
            const start = new Date(element.dataset.start);
            const end = element.dataset.end ? new Date(element.dataset.end) : null;
            if (Number.isNaN(start.getTime())) return;

            switch (element.dataset.portalScheduleTime) {
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

    window.PortalScheduleTime = { apply: apply };
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", function () { apply(document); });
    } else {
        apply(document);
    }
}());
