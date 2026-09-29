function setLanguageCookie(language) {
    const expiryDate = new Date();
    expiryDate.setDate(expiryDate.getDate() + 30);

    // Keep cookie in canonical casing (e.g., "en-US")
    document.cookie = `ASPNET_LANG=${language}; path=/; expires=${expiryDate.toUTCString()}; SameSite=Lax`;
}

function changeLanguage(newLang) {
    // Canonical cookie value (en-US / nl-NL)
    const canonicalLang = newLang;

    // Canonical URL segment (lowercase, matches your current URLs)
    const urlLang = newLang.toLowerCase(); // "en-us" / "nl-nl"

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
            event.preventDefault();

            const selectedLanguage = link.getAttribute("data-lang");
            if (!selectedLanguage || link.classList.contains("disabled")) {
                return;
            }

            changeLanguage(selectedLanguage);
        });
    });
});
