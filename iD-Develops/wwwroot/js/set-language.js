function setLanguageCookie(language) {
    const expiryDate = new Date();
    expiryDate.setDate(expiryDate.getDate() + 30);

    // Keep cookie in canonical casing (e.g., "en-US")
    document.cookie = `ASPNET_LANG=${language}; path=/; expires=${expiryDate.toUTCString()}; SameSite=Lax`;
}

function changeLanguage(newLang, targetHref) {
    // Canonical cookie value (en-US / nl-NL)
    const supportedLanguages = { "en-us": "en-US", "nl-nl": "nl-NL" };
    const canonicalLang = supportedLanguages[newLang?.toLowerCase()];
    if (!canonicalLang) return;
    if (targetHref && targetHref !== "#") {
        const destination = new URL(targetHref, window.location.href);
        destination.hash = window.location.hash;
        setLanguageCookie(canonicalLang);
        window.location.assign(destination.href);
        return;
    }

    // Canonical URL segment (lowercase, matches your current URLs)
    const urlLang = canonicalLang.toLowerCase(); // "en-us" / "nl-nl"

    const parts = window.location.pathname.split('/').filter(Boolean); // ["en-us","exams","manage"]
    const cultureRegex = /^[a-z]{2}-[a-z]{2}$/i;

    if (parts.length > 0 && cultureRegex.test(parts[0])) {
        // Replace existing culture segment (case-insensitive)
        parts[0] = urlLang;
    } else {
        // Prepend culture segment
        parts.unshift(urlLang);
    }

    setLanguageCookie(canonicalLang);

    const newUrl =
        window.location.origin +
        "/" + parts.join("/") +
        window.location.search +
        window.location.hash;

    window.location.assign(newUrl);
}

document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("[data-lang]").forEach(function (link) {
        link.addEventListener("click", function (event) {
            if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();

            const selectedLanguage = link.getAttribute("data-lang");
            if (!selectedLanguage || link.classList.contains("disabled")) {
                return;
            }

            changeLanguage(selectedLanguage, link.getAttribute("href"));
        });
    });
});
