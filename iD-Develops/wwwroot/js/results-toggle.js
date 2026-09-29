function toggleDetails(id) {
    var details = document.getElementById('details-' + id);
    if (details.style.display === "none" || details.style.display === "") {
        details.style.display = "block";
    } else {
        details.style.display = "none";
    }
}

// Initialize with first question details open if desired
document.addEventListener("DOMContentLoaded", function () {
    toggleDetails(1);
});