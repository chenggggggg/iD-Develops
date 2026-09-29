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
        document.body.appendChild(host);
        return host;
    }

    function ensureStyles() {
        if (document.getElementById("toast-styles")) return;

        const style = document.createElement("style");
        style.id = "toast-styles";
        style.textContent = `
#toast-host{
  position: fixed;
  top: 16px;
  right: 16px;
  z-index: 20000;
  display: flex;
  flex-direction: column;
  gap: 10px;
  pointer-events: none;
}

.toast-msg{
  pointer-events: auto;
  min-width: 280px;
  max-width: 380px;
  background: rgba(20,20,20,0.92);
  color: #fff;
  border-radius: 10px;
  box-shadow: 0 10px 30px rgba(0,0,0,0.25);
  padding: 12px 12px 10px 12px;
  display: grid;
  grid-template-columns: 1fr auto;
  grid-template-rows: auto auto;
  column-gap: 10px;
  row-gap: 6px;
  transform: translateY(-6px);
  opacity: 0;
  transition: transform 160ms ease, opacity 160ms ease;
}

.toast-msg.show{
  transform: translateY(0);
  opacity: 1;
}

.toast-title{
  font-size: 0.85rem;
  font-weight: 600;
  letter-spacing: .2px;
  opacity: .9;
}

.toast-body{
  grid-column: 1 / -1;
  font-size: 0.92rem;
  line-height: 1.25rem;
}

.toast-close{
  appearance: none;
  border: 0;
  background: transparent;
  color: rgba(255,255,255,0.85);
  font-size: 18px;
  line-height: 18px;
  padding: 4px 6px;
  border-radius: 8px;
  cursor: pointer;
}
.toast-close:hover{
  background: rgba(255,255,255,0.08);
}

.toast-bar{
  grid-column: 1 / -1;
  height: 3px;
  border-radius: 999px;
  background: rgba(255,255,255,0.18);
  overflow: hidden;
}

.toast-bar > i{
  display: block;
  height: 100%;
  width: 100%;
  transform-origin: left;
  transform: scaleX(1);
  background: rgba(255,255,255,0.65);
}

.toast-msg.success .toast-bar > i{ background: rgba(80, 200, 120, 0.85); }
.toast-msg.error   .toast-bar > i{ background: rgba(255, 90, 90, 0.85); }
.toast-msg.info    .toast-bar > i{ background: rgba(110, 180, 255, 0.85); }
.toast-msg.warn    .toast-bar > i{ background: rgba(255, 200, 80, 0.85); }

@media (max-width: 420px){
  #toast-host{ left: 12px; right: 12px; top: 12px; }
  .toast-msg{ max-width: none; width: 100%; }
}
    `;
        document.head.appendChild(style);
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
        ensureStyles();
        const host = ensureHost();

        const type = (opts.type || "info").toLowerCase(); // info|success|error|warn
        const title = opts.title || (type === "success" ? "Success" :
            type === "error" ? "Error" :
                type === "warn" ? "Warning" : "Notice");
        const durationMs = Number.isFinite(opts.durationMs) ? opts.durationMs : DEFAULT_DURATION_MS;

        const el = document.createElement("div");
        el.className = `toast-msg ${type}`;
        el.setAttribute("role", "status");

        el.innerHTML = `
      <div class="toast-title">${escapeHtml(title)}</div>
      <button class="toast-close" type="button" aria-label="Close">×</button>
      <div class="toast-body">${escapeHtml(message)}</div>
      <div class="toast-bar" aria-hidden="true"><i></i></div>
    `;

        host.appendChild(el);

        // animate in
        requestAnimationFrame(() => el.classList.add("show"));

        // progress bar animation (robust: force style flush before starting)
        const bar = el.querySelector(".toast-bar > i");
        if (bar && durationMs > 0) {
            bar.style.transition = "none";
            bar.style.transform = "scaleX(1)";

            // Force a reflow so the browser commits the initial state
            bar.getBoundingClientRect();

            bar.style.transition = `transform ${durationMs}ms linear`;

            // Start on next frame
            requestAnimationFrame(() => {
                bar.style.transform = "scaleX(0)";
            });
        }

        const close = () => {
            el.classList.remove("show");
            // allow transition to finish before remove
            setTimeout(() => el.remove(), 180);
        };

        el.querySelector(".toast-close")?.addEventListener("click", close);

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
