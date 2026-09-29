(() => {
    const initialize = () => {
        const elements = Array.from(document.querySelectorAll("[data-reveal]"));

        if (elements.length === 0) {
            return;
        }

        const reveal = (element) => {
            if (element.dataset.revealed === "true") {
                return;
            }

            element.dataset.revealed = "true";
            element.classList.remove("tw:opacity-0", "tw:-translate-y-full", "tw:translate-y-5", "tw:translate-y-6");
            element.classList.add("tw:opacity-100", "tw:translate-y-0");
        };

        const loadElements = elements.filter((element) => element.dataset.reveal === "load");
        requestAnimationFrame(() => {
            requestAnimationFrame(() => loadElements.forEach(reveal));
        });

        const scrollElements = elements.filter((element) => element.dataset.reveal !== "load");

        if (!("IntersectionObserver" in window)) {
            scrollElements.forEach(reveal);
            return;
        }

        const observer = new IntersectionObserver((entries) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) {
                    return;
                }

                reveal(entry.target);
                observer.unobserve(entry.target);
            });
        }, {
            rootMargin: "0px 0px -10% 0px",
            threshold: 0.12
        });

        scrollElements.forEach((element) => observer.observe(element));
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
