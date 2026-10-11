(() => {
    "use strict";

    const panel = document.querySelector(".todo-panel");
    if (!panel) return;
    const buttons = Array.from(panel.querySelectorAll(".todo-filter"));
    const list = panel.querySelector("#todo-list");
    // Capture original positions once so every filtering/sorting pass has stable tie-breaks.
    const todos = Array.from(list.querySelectorAll(".todo-item")).map((row, originalIndex) => ({
        row, originalIndex,
        type: row.dataset.todoType,
        status: row.dataset.todoStatus,
        priority: row.dataset.priority,
        dueDate: row.dataset.dueDate || ""
    }));
    const resultCount = panel.querySelector(".todo-result-count");
    const emptyMessage = panel.querySelector(".todo-empty");
    const dateReference = panel.querySelector("#todo-date-reference");

    // HTML contains six de-identified Demo tasks. No shared state, persistence or API.
    // Todo type/status/dueDate are NOT case status or case updatedAt.
    // Future data should reference a case and an independent work item; never infer a due date
    // from case updatedAt, and never mark a case completed because a task/form is completed.
    function localToday() {
        const today = new Date();
        return [today.getFullYear(), String(today.getMonth() + 1).padStart(2, "0"),
            String(today.getDate()).padStart(2, "0")].join("-");
    }

    function isOverdue(todo, today) {
        return todo.status !== "completed" && Boolean(todo.dueDate) && todo.dueDate < today;
    }

    function compareTodos(a, b, today) {
        const priorityOrder = Number(b.priority === "high") - Number(a.priority === "high");
        if (priorityOrder) return priorityOrder;
        const overdueOrder = Number(isOverdue(b, today)) - Number(isOverdue(a, today));
        if (overdueOrder) return overdueOrder;
        // Local ISO calendar dates: earlier deadlines first; undated items last.
        const dueOrder = (a.dueDate || "9999-12-31").localeCompare(b.dueDate || "9999-12-31");
        return dueOrder || a.originalIndex - b.originalIndex;
    }

    function applyFilter(button) {
        const today = localToday();
        const type = button.dataset.filterType;
        let visibleCount = 0;
        [...todos].sort((a, b) => compareTodos(a, b, today)).forEach((todo) => {
            const matches = type === "all" || todo.type === type;
            todo.row.hidden = !matches;
            todo.row.querySelector(".todo-overdue").hidden = !isOverdue(todo, today);
            list.appendChild(todo.row);
            if (matches) visibleCount += 1;
        });
        buttons.forEach((filterButton) => {
            const selected = filterButton === button;
            filterButton.classList.toggle("is-selected", selected);
            filterButton.setAttribute("aria-pressed", String(selected));
        });
        resultCount.textContent = `${button.textContent.trim()}：顯示 ${visibleCount} 筆 Demo 待辦`;
        emptyMessage.hidden = visibleCount > 0;
        dateReference.textContent = "Demo 日期判斷基準（本機日期）：" + today.replaceAll("-", "/");
    }

    buttons.forEach((button) => button.addEventListener("click", () => applyFilter(button)));
    const allButton = buttons.find((button) => button.dataset.filterType === "all");
    if (allButton) applyFilter(allButton);
})();
