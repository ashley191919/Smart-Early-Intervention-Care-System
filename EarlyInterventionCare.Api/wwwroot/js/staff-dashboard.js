(() => {
    "use strict";

    const panel = document.querySelector(".follow-up-panel");
    if (!panel) return;

    const buttons = Array.from(panel.querySelectorAll(".follow-up-filter"));
    const cases = Array.from(panel.querySelectorAll(".follow-up-case"));
    const caseList = panel.querySelector("#follow-up-list");
    const resultCount = panel.querySelector(".follow-up-result-count");
    const emptyMessage = panel.querySelector(".follow-up-empty");
    function applyFilter(button) {
        const status = button.dataset.filterValue;
        // Priority controls sorting only; every filter targets processing status.
        const sortedCases = [...cases].sort((a, b) => {
            if (status !== "completed") {
                const priorityOrder = Number(b.dataset.priority === "high") -
                    Number(a.dataset.priority === "high");
                if (priorityOrder !== 0) return priorityOrder;
            }

            // ISO dates (YYYY-MM-DD) sort chronologically without timezone conversion.
            return b.dataset.updated.localeCompare(a.dataset.updated);
        });
        let visibleCount = 0;

        sortedCases.forEach((caseRow) => {
            const matches = status === "all" || caseRow.dataset.status === status;
            caseRow.hidden = !matches;
            caseList.appendChild(caseRow);
            if (matches) visibleCount += 1;
        });

        buttons.forEach((filterButton) => {
            const selected = filterButton === button;
            filterButton.classList.toggle("is-selected", selected);
            filterButton.setAttribute("aria-pressed", String(selected));
        });

        resultCount.textContent = `${button.textContent.trim()}：顯示 ${visibleCount} 筆個案`;
        emptyMessage.hidden = visibleCount > 0;
    }

    buttons.forEach((button) => {
        button.addEventListener("click", () => {
            applyFilter(button);
        });
    });

    const allButton = buttons.find((button) => button.dataset.filterValue === "all");
    applyFilter(allButton);
})();
