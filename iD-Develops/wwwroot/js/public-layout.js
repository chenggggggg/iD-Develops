document.addEventListener("DOMContentLoaded", function () {
    const menu = document.querySelector("[data-public-menu]");
    const backdrop = document.querySelector("[data-public-menu-backdrop]");
    const menuToggle = document.querySelector("[data-public-menu-toggle]");
    const menuCloseButtons = document.querySelectorAll("[data-public-menu-close]");
    const languageToggle = document.querySelector("[data-public-language-toggle]");
    const languageMenu = document.querySelector("[data-public-language-menu]");
    const servicesMenu = document.querySelector("[data-services-menu]");
    const servicesToggle = document.querySelector("[data-services-toggle]");

    function setServicesMenuOpen(isOpen) {
        if (!servicesMenu || !servicesToggle) {
            return;
        }

        servicesMenu.dataset.open = String(isOpen);
        servicesToggle.setAttribute("aria-expanded", String(isOpen));
    }

    servicesToggle?.addEventListener("click", function () {
        setServicesMenuOpen(servicesMenu?.dataset.open !== "true");
    });

    function setMenuOpen(isOpen) {
        if (!menu || !backdrop || !menuToggle) {
            return;
        }

        menu.hidden = !isOpen;
        backdrop.hidden = !isOpen;
        menuToggle.setAttribute("aria-expanded", String(isOpen));
        menuToggle.dataset.open = String(isOpen);
        document.body.classList.toggle("public-menu-open", isOpen);
    }

    menuToggle?.addEventListener("click", function () {
        setMenuOpen(menu?.hidden !== false);
    });

    backdrop?.addEventListener("click", function () {
        setMenuOpen(false);
    });

    menuCloseButtons.forEach(function (button) {
        button.addEventListener("click", function () {
            setMenuOpen(false);
        });
    });

    languageToggle?.addEventListener("click", function (event) {
        event.preventDefault();

        if (!languageMenu) {
            return;
        }

        const isOpen = languageMenu.hidden;
        languageMenu.hidden = !isOpen;
        languageToggle.setAttribute("aria-expanded", String(isOpen));
    });

    document.addEventListener("click", function (event) {
        if (servicesMenu && servicesMenu.dataset.open === "true" && !servicesMenu.contains(event.target)) {
            setServicesMenuOpen(false);
        }

        if (!languageMenu || !languageToggle || languageMenu.hidden) {
            return;
        }

        if (languageMenu.contains(event.target) || languageToggle.contains(event.target)) {
            return;
        }

        languageMenu.hidden = true;
        languageToggle.setAttribute("aria-expanded", "false");
    });

    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") {
            return;
        }

        setMenuOpen(false);
        setServicesMenuOpen(false);

        if (languageMenu && languageToggle) {
            languageMenu.hidden = true;
            languageToggle.setAttribute("aria-expanded", "false");
        }
    });
});
