document.addEventListener("DOMContentLoaded", function () {
    const productTypeSelect = document.getElementById("Input_ProductType");

    function applyDefaultJourneyForProductType() {
        if (!productTypeSelect) {
            return;
        }

        const defaults = {
            "1": { collect: true, booking: false, payment: false },
            "2": { collect: true, booking: true, payment: false },
            "3": { collect: true, booking: false, payment: true },
            "4": { collect: true, booking: false, payment: true }
        };

        const selected = defaults[productTypeSelect.value];
        if (!selected) {
            return;
        }

        document.getElementById("Input_CollectCustomerDetails").checked = selected.collect;
        document.getElementById("Input_SendToBookingPage").checked = selected.booking;
        document.getElementById("Input_TakePaymentNow").checked = selected.payment;

        const formCheckbox = document.getElementById("Input_CollectCustomerDetails");
        const bookingCheckbox = document.getElementById("Input_SendToBookingPage");
        const paymentCheckbox = document.getElementById("Input_TakePaymentNow");

        if (formCheckbox) {
            formCheckbox.disabled = productTypeSelect.value === "1";
        }

        if (bookingCheckbox) {
            bookingCheckbox.disabled = productTypeSelect.value === "1" || productTypeSelect.value === "2" || productTypeSelect.value === "3";
            if (productTypeSelect.value === "2") {
                bookingCheckbox.checked = true;
            }
        }

        if (paymentCheckbox) {
            paymentCheckbox.disabled = productTypeSelect.value === "1" || productTypeSelect.value === "2";
        }
    }

    if (window.TinyMceHelpers) {
        TinyMceHelpers.configureJQueryValidationForTinyMce();
        TinyMceHelpers.initEditors(["#summary-editor-create"], {
            height: 220,
            plugins: "lists link paste",
            toolbar: "bold italic underline | bullist numlist | link | removeformat"
        });
        TinyMceHelpers.wireTriggerSaveOnSubmit("#product-create-form");
    }

    if (productTypeSelect) {
        productTypeSelect.addEventListener("change", applyDefaultJourneyForProductType);
    }

    applyDefaultJourneyForProductType();
});
