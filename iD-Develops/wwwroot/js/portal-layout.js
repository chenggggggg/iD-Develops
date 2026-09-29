document.addEventListener("DOMContentLoaded", function () {
    const sidebar = document.querySelector("[data-portal-sidebar]");
    const backdrop = document.querySelector("[data-portal-sidebar-backdrop]");
    const toggle = document.querySelector("[data-portal-sidebar-toggle]");
    const closeButtons = document.querySelectorAll("[data-portal-sidebar-close]");

    if (!sidebar || !backdrop || !toggle) {
        return;
    }

    const profileToggles = document.querySelectorAll("[data-portal-profile-toggle]");

    function positionProfileMenu(profileToggle, menu) {
        const triggerRect = profileToggle.getBoundingClientRect();
        const viewportGap = 8;
        const triggerGap = 6;
        const menuWidth = menu.offsetWidth;
        const menuHeight = menu.offsetHeight;
        const maxLeft = window.innerWidth - menuWidth - viewportGap;
        const preferredTop = triggerRect.bottom + triggerGap;
        const shouldOpenAbove = preferredTop + menuHeight > window.innerHeight - viewportGap;
        const top = shouldOpenAbove
            ? Math.max(viewportGap, triggerRect.top - menuHeight - triggerGap)
            : preferredTop;
        const left = Math.min(Math.max(triggerRect.left, viewportGap), Math.max(viewportGap, maxLeft));

        menu.style.top = `${top}px`;
        menu.style.left = `${left}px`;
    }

    function updateOpenProfileMenus() {
        profileToggles.forEach(function (profileToggle) {
            const profile = profileToggle.closest(".portal-profile");
            const menu = profile ? profile.querySelector("[data-portal-profile-menu]") : null;

            if (!menu || menu.hidden) {
                return;
            }

            positionProfileMenu(profileToggle, menu);
        });
    }

    function closeProfileMenus(exceptToggle) {
        profileToggles.forEach(function (profileToggle) {
            if (profileToggle === exceptToggle) {
                return;
            }

            const profile = profileToggle.closest(".portal-profile");
            const menu = profile ? profile.querySelector("[data-portal-profile-menu]") : null;

            if (!menu) {
                return;
            }

            menu.hidden = true;
            menu.style.top = "";
            menu.style.left = "";
            profileToggle.setAttribute("aria-expanded", "false");
        });
    }

    profileToggles.forEach(function (profileToggle) {
        const profile = profileToggle.closest(".portal-profile");
        const menu = profile ? profile.querySelector("[data-portal-profile-menu]") : null;

        if (!menu) {
            return;
        }

        profileToggle.addEventListener("click", function (event) {
            event.stopPropagation();
            const shouldOpen = menu.hidden;

            closeProfileMenus(profileToggle);

            if (shouldOpen) {
                menu.hidden = false;
                positionProfileMenu(profileToggle, menu);
            } else {
                menu.hidden = true;
                menu.style.top = "";
                menu.style.left = "";
            }

            profileToggle.setAttribute("aria-expanded", shouldOpen ? "true" : "false");
        });

        menu.addEventListener("click", function (event) {
            event.stopPropagation();
        });
    });

    document.addEventListener("click", function () {
        closeProfileMenus();
    });

    window.addEventListener("resize", updateOpenProfileMenus);
    document.addEventListener("scroll", updateOpenProfileMenus, true);

    function setOpen(isOpen) {
        sidebar.hidden = !isOpen;
        backdrop.hidden = !isOpen;
        toggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
        document.body.classList.toggle("portal-sidebar-open", isOpen);

        if (!isOpen) {
            closeProfileMenus();
        }
    }

    toggle.addEventListener("click", function () {
        setOpen(sidebar.hidden);
    });

    backdrop.addEventListener("click", function () {
        setOpen(false);
    });

    closeButtons.forEach(function (button) {
        button.addEventListener("click", function () {
            setOpen(false);
        });
    });

    sidebar.querySelectorAll("a").forEach(function (link) {
        link.addEventListener("click", function () {
            setOpen(false);
        });
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && !sidebar.hidden) {
            setOpen(false);
        }

        if (event.key === "Escape") {
            closeProfileMenus();
        }
    });

    function setDialogOpen(dialogId, isOpen) {
        const dialog = document.getElementById(dialogId);
        const dialogBackdrop = document.querySelector(`[data-portal-dialog-backdrop="${dialogId}"]`);
        if (!dialog || !dialogBackdrop) {
            return;
        }

        dialog.hidden = !isOpen;
        dialogBackdrop.hidden = !isOpen;
        document.body.classList.toggle("portal-dialog-open", isOpen);

        if (isOpen) {
            dialog.focus();
        }
    }

    document.querySelectorAll("[data-portal-dialog-open]").forEach(function (button) {
        button.addEventListener("click", function () {
            setDialogOpen(button.dataset.portalDialogOpen, true);
        });
    });

    document.querySelectorAll("[data-portal-dialog-close]").forEach(function (button) {
        button.addEventListener("click", function () {
            setDialogOpen(button.dataset.portalDialogClose, false);
        });
    });

    document.querySelectorAll("[data-portal-dialog-backdrop]").forEach(function (dialogBackdrop) {
        dialogBackdrop.addEventListener("click", function () {
            setDialogOpen(dialogBackdrop.dataset.portalDialogBackdrop, false);
        });
    });

    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") {
            return;
        }

        document.querySelectorAll(".portal-dialog:not([hidden])").forEach(function (dialog) {
            setDialogOpen(dialog.id, false);
        });
    });
});
