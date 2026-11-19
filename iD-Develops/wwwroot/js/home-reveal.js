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
            element.classList.remove("opacity-0", "-translate-y-full", "translate-y-5", "translate-y-6");
            element.classList.add("opacity-100", "translate-y-0");
        };

        const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
        elements.forEach((element) => {
            // Prepare the hidden state without animating visible content out first.
            element.classList.add("transition-none!", "motion-reduce:transition-none");
            element.dataset.revealed = "false";
            element.addEventListener("focusin", () => reveal(element), { once: true });
        });

        if (reducedMotion.matches || typeof IntersectionObserver !== "function") {
            elements.forEach(reveal);
            return;
        }

        elements.forEach((element) => {
            // Commit the starting state before enabling the shared CSS entrance transition.
            getComputedStyle(element).opacity;
            element.classList.remove("transition-none!");
        });

        const loadElements = elements.filter((element) => element.dataset.reveal === "load");
        requestAnimationFrame(() => {
            requestAnimationFrame(() => loadElements.forEach(reveal));
        });

        const scrollElements = elements.filter((element) => element.dataset.reveal !== "load");

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

        reducedMotion.addEventListener("change", (event) => {
            if (event.matches) {
                elements.forEach(reveal);
                observer.disconnect();
            }
        });
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
