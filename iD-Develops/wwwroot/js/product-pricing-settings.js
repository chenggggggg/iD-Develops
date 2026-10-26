function initializeProductPricingSettings() {
    const form = document.getElementById("product-edit-form");
    if (!form) return;
    const name = form.querySelector('[name="EditInput.Name"]');
    const slug = form.querySelector('[name="EditInput.Slug"]');
    const automatic = form.querySelector('[type="checkbox"][name="EditInput.UpdateSlugFromName"]');
    const slugify = value => value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 200).replace(/-$/, "");
    name?.addEventListener("input", () => { if (automatic?.checked && slug) slug.value = slugify(name.value); });
    slug?.addEventListener("input", () => { if (automatic) automatic.checked = false; });
    automatic?.addEventListener("change", () => { if (automatic.checked && slug && name) slug.value = slugify(name.value); });

    const type = form.querySelector('[name="EditInput.DiscountType"]');
    const value = form.querySelector('[name="EditInput.DiscountValue"]');
    function updateDiscountControl() {
        if (!type || !value) return;
        const enabled = type.value !== "None";
        value.disabled = !enabled;
        value.required = enabled;
        if (type.value === "Percentage") value.max = "100";
        else value.removeAttribute("max");
    }
    type?.addEventListener("change", updateDiscountControl);
    updateDiscountControl();
    if (automatic) automatic.disabled = false;
}

if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initializeProductPricingSettings, { once: true });
else initializeProductPricingSettings();
