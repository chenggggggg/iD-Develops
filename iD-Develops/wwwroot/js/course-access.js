document.addEventListener("DOMContentLoaded", function () {
    const search = document.querySelector("[data-course-assignee-search]");
    const options = Array.from(document.querySelectorAll("[data-course-assignee]"));
    const empty = document.querySelector("[data-course-assignee-empty]");

    if (!search || options.length === 0) {
        return;
    }

    search.addEventListener("input", function () {
        const query = search.value.trim().toLowerCase();
        let visibleCount = 0;

        options.forEach(function (option) {
            const visible = query.length > 0 && option.dataset.courseAssignee.includes(query);
            option.hidden = !visible;
            visibleCount += visible ? 1 : 0;
        });

        if (empty) {
            empty.hidden = visibleCount > 0;
            empty.textContent = query.length > 0
                ? "No users match your search."
                : "Use search to find a user.";
        }
    });
});
