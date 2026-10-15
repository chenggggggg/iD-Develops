// wwwroot/js/display-messages.js
(() => {
    "use strict";

    const DEFAULT_DURATION_MS = 12000;

    function ensureHost() {
        let host = document.getElementById("toast-host");
        if (host) return host;

        host = document.createElement("div");
        host.id = "toast-host";
        host.setAttribute("aria-live", "polite");
        host.setAttribute("aria-atomic", "true");
        host.className = "pointer-events-none fixed top-4 right-4 z-[20000] flex w-[min(24rem,calc(100%-2rem))] flex-col gap-2 max-[420px]:right-2 max-[420px]:left-2 max-[420px]:w-auto";
        document.body.appendChild(host);
        return host;
    }

    function escapeHtml(s) {
        return String(s ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function showToast(message, opts = {}) {
        const host = ensureHost();

        const requestedType = (opts.type || "info").toLowerCase();
        const type = requestedType === "warning" ? "warn" : requestedType;
        const title = opts.title || (type === "success" ? "Success" :
            type === "error" ? "Error" :
                type === "warn" ? "Warning" : "Notice");
        const durationMs = Number.isFinite(opts.durationMs) ? opts.durationMs : DEFAULT_DURATION_MS;
        const variants = {
            success: {
                borderClass: "border-emerald-200",
                iconClass: "bg-emerald-50 text-emerald-700",
                icon: "fa-check"
            },
            error: {
                borderClass: "border-red-200",
                iconClass: "bg-red-50 text-red-700",
                icon: "fa-exclamation"
            },
            warn: {
                borderClass: "border-amber-200",
                iconClass: "bg-amber-50 text-amber-700",
                icon: "fa-triangle-exclamation"
            },
            info: {
                borderClass: "border-sky-200",
                iconClass: "bg-sky-50 text-sky-700",
                icon: "fa-circle-info"
            }
        };
        const variant = variants[type] || variants.info;

        const el = document.createElement("div");
        el.className = `pointer-events-auto -translate-y-1 rounded-xl border ${variant.borderClass} bg-white p-4 text-slate-800 opacity-0 shadow-[0_18px_45px_rgba(15,23,42,0.16)] transition-[opacity,translate] duration-200`;
        el.setAttribute("role", type === "error" ? "alert" : "status");

        el.innerHTML = `
            <div class="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-3">
                <span class="grid size-9 place-items-center rounded-full ${variant.iconClass}" aria-hidden="true">
                    <i class="fa-solid ${variant.icon}"></i>
                </span>
                <div class="min-w-0">
                    <div class="text-sm font-semibold text-slate-950">${escapeHtml(title)}</div>
                    <div class="mt-0.5 text-sm leading-5 text-slate-600">${escapeHtml(message)}</div>
                </div>
                <button data-toast-close class="grid size-7 place-items-center rounded-md border-0 bg-transparent text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#005190]" type="button" aria-label="Close">
                    <i class="fa-solid fa-xmark" aria-hidden="true"></i>
                </button>
            </div>`;

        host.appendChild(el);

        requestAnimationFrame(() => {
            el.classList.remove("-translate-y-1", "opacity-0");
            el.classList.add("translate-y-0", "opacity-100");
        });

        const close = () => {
            el.classList.remove("translate-y-0", "opacity-100");
            el.classList.add("-translate-y-1", "opacity-0");
            setTimeout(() => el.remove(), 200);
        };

        el.querySelector("[data-toast-close]")?.addEventListener("click", close);

        if (durationMs > 0) {
            setTimeout(close, durationMs);
        }

        return el;
    }

    // Backward-compatible wrappers (so you don’t have to refactor all call sites immediately)
    window.displayPopupMessage = function (message, isSuccess) {
        showToast(message, { type: isSuccess ? "success" : "error" });
    };

    window.displayDivMessage = function (message, isSuccess) {
        showToast(message, { type: isSuccess ? "success" : "error" });
    };

    // New preferred API
    window.notify = showToast;

    document.addEventListener("DOMContentLoaded", () => {
        const successMessage = sessionStorage.getItem("successMessage");
        if (successMessage) {
            showToast(successMessage, { type: "success", title: "Saved" });
            sessionStorage.removeItem("successMessage");
        }
    });
})();
