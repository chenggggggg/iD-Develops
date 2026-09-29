function changeExamsPerPage() {
    var examsPerPageEl = document.getElementById("examsPerPage");
    if (!examsPerPageEl) return;

    var examsPerPage = examsPerPageEl.value;

    // Keep your culture handling
    var currentCulture = window.location.pathname.split('/')[1] || "";

    // IMPORTANT:
    // Do not use @Url.Page(...) inside a static .js file.
    // Instead, redirect to the same page with a query param, preserving existing query params.
    var url = new URL(window.location.href);
    url.searchParams.set("examsPerPage", examsPerPage);

    // If your culture is part of the path and URL() kept it, this is fine.
    // If you need to force culture, you'd do it server-side (recommended).
    window.location.href = url.toString();
}

function deleteExam(element) {
    if (!element) return;

    // New approach:
    // The server renders the correct delete endpoint on the button.
    // Example: data-delete-url="/en/Exams/Manage/Index?handler=DeleteExam&examId=123"
    var deleteUrl = element.getAttribute("data-delete-url");

    // If there's no delete URL, this is not a manage page row.
    // This makes the function safe to exist on student pages.
    if (!deleteUrl) {
        notify("Delete failed. Please refresh and try again.", { type: "error", title: "Delete failed" });
        return;
    }

    // Anti-forgery token:
    // Prefer the standard Razor token input: <input name="__RequestVerificationToken" ...>
    // But keep backward-compat with your existing id if you still use it.
    var tokenInput =
        document.querySelector('input[name="__RequestVerificationToken"]') ||
        document.getElementById("RequestVerificationToken");

    if (!tokenInput || !tokenInput.value) {
        notify("Anti-forgery token not found.", { type: "error", title: "Token error" });
        return;
    }

    if (!confirm("Are you sure you want to delete this exam?")) {
        return;
    }

    $.ajax({
        type: "POST",
        url: deleteUrl,
        headers: {
            RequestVerificationToken: tokenInput.value
        },
        success: function (response) {
            if (window.notify) {
                window.notify(
                    (response && response.message) ? response.message : "Exam deleted successfully",
                    { type: "success", title: "Deleted" }
                );
            }

            // Remove the row
            $(element).closest("tr").remove();
        },
        error: function (error) {
            if (window.notify) {
                window.notify("Error removing exam", { type: "error", title: "Delete failed" });
            }
        }
    });
}
