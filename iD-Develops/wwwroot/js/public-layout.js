function initializePublicLayout() {
    const menu = document.querySelector("[data-public-menu]");
    const menuToggle = document.querySelector("[data-public-menu-toggle]");
    const menuCloseButtons = document.querySelectorAll("[data-public-menu-close]");
    const languageToggle = document.querySelector("[data-public-language-toggle]");
    const languageMenu = document.querySelector("[data-public-language-menu]");
    const servicesMenu = document.querySelector("[data-services-menu]");
    const servicesToggle = document.querySelector("[data-services-toggle]");
    const desktopNavigation = window.matchMedia("(min-width: 64rem)");

    function syncMenuState() {
        const isOpen = menu?.open === true;
        menuToggle?.setAttribute("aria-expanded", String(isOpen));
        if (menuToggle) menuToggle.dataset.open = String(isOpen);
        document.body.classList.toggle("overflow-hidden", isOpen);
    }

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
        if (!menu || !menuToggle) {
            return;
        }

        if (isOpen && !menu.open && !desktopNavigation.matches) {
            menu.showModal();
            menu.querySelector("[data-public-menu-close]")?.focus({ preventScroll: true });
        } else if (!isOpen && menu.open) {
            menu.close();
            if (!desktopNavigation.matches) menuToggle.focus({ preventScroll: true });
        }
        syncMenuState();
    }

    menuToggle?.addEventListener("click", function () {
        setMenuOpen(!menu?.open);
    });

    menu?.addEventListener("close", syncMenuState);

    menu?.addEventListener("keydown", function (event) {
        if (event.key !== "Tab") return;

        const controls = Array.from(menu.querySelectorAll("a[href], button:not(:disabled)"))
            .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0);
        const first = controls[0];
        const last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
        }
    });

    // A native modal escapes the header's blur/transform containing block, keeps
    // background content inert, and contains keyboard focus in the navigation.
    let pressedBackdrop = false;
    menu?.addEventListener("pointerdown", function (event) {
        const bounds = menu.getBoundingClientRect();
        pressedBackdrop = event.target === menu && (
            event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom
        );
    });

    menu?.addEventListener("click", function (event) {
        const followsLink = event.target.closest("a[href]") &&
            !event.defaultPrevented && event.button === 0 &&
            !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
        if ((pressedBackdrop && event.target === menu) || followsLink) setMenuOpen(false);
        pressedBackdrop = false;
    });

    desktopNavigation.addEventListener("change", function (event) {
        if (event.matches) setMenuOpen(false);
    });

    window.addEventListener("pagehide", function () {
        if (menu?.open) setMenuOpen(false);
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
    syncMenuState();
    if (menuToggle) menuToggle.disabled = false;
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializePublicLayout, { once: true });
} else {
    initializePublicLayout();
}
