(() => {
    "use strict";

    const caseStatuses = Object.freeze({ "pending-contact": "待聯繫", "in-progress": "進行中", completed: "已完成" });
    function calendarDate(value) {
        if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return "";
        const date = new Date(value + "T12:00:00");
        if (!Number.isFinite(date.getTime())) return "";
        const local = [date.getFullYear(), String(date.getMonth() + 1).padStart(2, "0"), String(date.getDate()).padStart(2, "0")].join("-");
        return local === value ? value : "";
    }
    function sortReminders(items, status = "all") {
        // Display-only ordering: priority, explicitly supplied update date, stable ties.
        // Assessment dates and reminder wording never participate in sorting.
        return [...items].sort((a, b) =>
            (status === "completed" ? 0 : Number(b.priority === "high") - Number(a.priority === "high")) ||
            (b.updatedAt || "").localeCompare(a.updatedAt || "") || a.originalIndex - b.originalIndex);
    }
    if (typeof module === "object" && module.exports) module.exports = { caseStatuses, calendarDate, sortReminders };
    if (typeof document === "undefined") return;
    const panel = document.querySelector(".todo-panel");
    if (!panel) return;
    const params = new URLSearchParams(location.search);
    const apiMode = window.MedicalApiConfig?.caseSource === "api" || params.has("source") || params.has("caseId");
    if (apiMode) {
        // There is no approved aggregate/reminder producer yet. Never infer totals from
        // a paginated Case response or show fixture counts as official statistics.
        document.querySelectorAll(".dashboard-stat-value").forEach(value => { value.textContent = "未提供"; });
        panel.querySelector("#todo-list").replaceChildren();
        panel.querySelectorAll(".todo-filter").forEach(button => { button.disabled = true; });
        panel.querySelector(".todo-result-count").textContent = "未提供";
        panel.querySelector(".todo-filter-hint").textContent = "";
        const empty = panel.querySelector(".todo-empty");
        empty.textContent = "提醒資料尚未提供"; empty.hidden = false;
        return;
    }
    const caseButtons = Array.from(panel.querySelectorAll("[data-filter-case-status]"));
    const list = panel.querySelector("#todo-list");
    // Capture original positions once so every filtering/sorting pass has stable tie-breaks.
    const todos = Array.from(list.querySelectorAll(".todo-item")).map((row, originalIndex) => ({
        row, originalIndex,
        // Original task priority stays intact for its own workflow.
        priority: row.dataset.casePriority,
        updatedAt: calendarDate(row.dataset.updatedAt),
        caseStatus: row.dataset.caseStatus || "",
        assessmentDate: calendarDate(row.dataset.assessmentDate)
    }));
    const resultCount = panel.querySelector(".todo-result-count");
    const emptyMessage = panel.querySelector(".todo-empty");
    // Synthetic reminder text is supplied directly in HTML, not calculated or persisted.
    // Existing todo status/deadline attributes remain for Header compatibility only.
    let selectedCaseStatus = "all";
    todos.forEach(todo => {
        const updated = todo.row.querySelector(".todo-updated-at");
        updated.textContent = todo.updatedAt ? todo.updatedAt.replaceAll("-", "/") : "未提供";
        if (todo.updatedAt) updated.setAttribute("datetime", todo.updatedAt);
        todo.row.querySelector(".todo-priority").hidden = todo.priority !== "high";
        todo.row.querySelector(".todo-normal-priority").hidden = todo.priority === "high";
        const date = todo.row.querySelector(".todo-assessment-date");
        todo.row.querySelector(".todo-assessment").hidden = !todo.assessmentDate;
        date.textContent = todo.assessmentDate ? todo.assessmentDate.replaceAll("-", "/") : "";
        if (todo.assessmentDate) date.setAttribute("datetime", todo.assessmentDate);
        todo.row.querySelector(".todo-case-status").textContent = Object.hasOwn(caseStatuses, todo.caseStatus) ? caseStatuses[todo.caseStatus] : "未提供";
    });
    function applyFilters() {
        let visibleCount = 0;
        sortReminders(todos, selectedCaseStatus).forEach((todo) => {
            const matches = selectedCaseStatus === "all" || todo.caseStatus === selectedCaseStatus;
            todo.row.hidden = !matches;
            list.appendChild(todo.row);
            if (matches) visibleCount += 1;
        });
        caseButtons.forEach(button => {
            const selected = button.dataset.filterCaseStatus === selectedCaseStatus;
            button.classList.toggle("is-selected", selected);
            button.setAttribute("aria-pressed", String(selected));
        });
        resultCount.textContent = `${caseStatuses[selectedCaseStatus] || "全部"}：顯示 ${visibleCount} 筆提醒`;
        emptyMessage.hidden = visibleCount > 0;
    }

    caseButtons.forEach(button => button.addEventListener("click", () => { selectedCaseStatus = button.dataset.filterCaseStatus; applyFilters(); }));
    applyFilters();
})();
