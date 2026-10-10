function initializeProductAdminList() {
    const tabList = document.querySelector("[data-product-list-tabs]");
    if (!tabList) {
        return;
    }

    const tabs = Array.from(tabList.querySelectorAll("[data-product-list-tab]"));
    const panels = Array.from(document.querySelectorAll("[data-product-list-panel]"));

    function activateTab(tab, updateUrl) {
        const targetId = tab?.dataset.productListTarget;
        if (!targetId) {
            return;
        }

        tabs.forEach(function (candidate) {
            const isActive = candidate === tab;
            candidate.setAttribute("aria-selected", isActive ? "true" : "false");
            candidate.tabIndex = isActive ? 0 : -1;
        });

        panels.forEach(function (panel) {
            panel.hidden = panel.id !== targetId;
        });

        if (updateUrl && window.history?.replaceState) {
            const url = new URL(window.location.href);
            url.searchParams.set("tab", tab.dataset.productListKey || "all");
            window.history.replaceState({}, "", url);
        }
    }

    tabs.forEach(function (tab, index) {
        tab.addEventListener("click", function () {
            activateTab(tab, true);
        });

        tab.addEventListener("keydown", function (event) {
            let targetIndex = null;
            if (event.key === "ArrowRight") {
                targetIndex = (index + 1) % tabs.length;
            } else if (event.key === "ArrowLeft") {
                targetIndex = (index - 1 + tabs.length) % tabs.length;
            } else if (event.key === "Home") {
                targetIndex = 0;
            } else if (event.key === "End") {
                targetIndex = tabs.length - 1;
            }

            if (targetIndex === null) {
                return;
            }

            event.preventDefault();
            const targetTab = tabs[targetIndex];
            activateTab(targetTab, true);
            targetTab.focus();
        });
    });

    const selectedTab = tabs.find(function (tab) {
        return tab.getAttribute("aria-selected") === "true";
    }) || tabs[0];
    activateTab(selectedTab, false);
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeProductAdminList, { once: true });
} else {
    initializeProductAdminList();
}
