(() => {
    "use strict";

    // De-identified front-end Demo data. Replace this source when an API is added.
    const demoCases = [
        { id: "DEMO-001", name: "王○安", status: "in-progress", priority: "high", contactName: "王○美", updatedAt: "2026-10-06" },
        { id: "DEMO-002", name: "陳○恩", status: "in-progress", priority: "normal", contactName: "陳○芬", updatedAt: "2026-10-05" },
        { id: "DEMO-003", name: "林○宇", status: "pending-contact", priority: "normal", contactName: "林○婷", updatedAt: "2026-10-04" },
        { id: "DEMO-004", name: "張○庭", status: "in-progress", priority: "high", contactName: "張○華", updatedAt: "2026-10-03" },
        { id: "DEMO-005", name: "李○安", status: "completed", priority: "normal", contactName: "李○玲", updatedAt: "2026-10-02" },
        { id: "DEMO-006", name: "黃○恩", status: "in-progress", priority: "normal", contactName: "黃○珍", updatedAt: "2026-10-01" },
        { id: "DEMO-007", name: "吳○辰", status: "pending-contact", priority: "high", contactName: "吳○萱", updatedAt: "2026-09-30" },
        { id: "DEMO-008", name: "劉○晴", status: "in-progress", priority: "normal", contactName: "劉○雅", updatedAt: "2026-09-29" },
        { id: "DEMO-009", name: "蔡○睿", status: "completed", priority: "normal", contactName: "蔡○慧", updatedAt: "2026-09-28" },
        { id: "DEMO-010", name: "楊○樂", status: "completed", priority: "normal", contactName: "楊○君", updatedAt: "2026-09-27" },
        { id: "DEMO-011", name: "王○涵", status: "pending-contact", priority: "normal", contactName: "王○芳", updatedAt: "2026-09-26" },
        { id: "DEMO-012", name: "陳○佑", status: "in-progress", priority: "high", contactName: "陳○蓉", updatedAt: "2026-09-25" },
        { id: "DEMO-013", name: "林○希", status: "completed", priority: "normal", contactName: "林○如", updatedAt: "2026-09-24" },
        { id: "DEMO-014", name: "張○軒", status: "in-progress", priority: "normal", contactName: "張○惠", updatedAt: "2026-09-23" },
        { id: "DEMO-015", name: "李○妍", status: "completed", priority: "high", contactName: "李○真", updatedAt: "2026-09-22" },
        { id: "DEMO-016", name: "黃○皓", status: "completed", priority: "normal", contactName: "黃○宜", updatedAt: "2026-09-21" },
        { id: "DEMO-017", name: "吳○彤", status: "pending-contact", priority: "normal", contactName: "吳○雯", updatedAt: "2026-09-20" },
        { id: "DEMO-018", name: "劉○宸", status: "in-progress", priority: "normal", contactName: "劉○敏", updatedAt: "2026-09-19" },
        { id: "DEMO-019", name: "蔡○綺", status: "completed", priority: "normal", contactName: "蔡○靜", updatedAt: "2026-09-18" },
        { id: "DEMO-020", name: "楊○哲", status: "completed", priority: "normal", contactName: "楊○怡", updatedAt: "2026-09-17" },
        { id: "DEMO-021", name: "王○寧", status: "completed", priority: "normal", contactName: "王○琪", updatedAt: "2026-09-16" },
        { id: "DEMO-022", name: "陳○霖", status: "completed", priority: "normal", contactName: "陳○瑄", updatedAt: "2026-09-15" },
        { id: "DEMO-023", name: "林○柔", status: "completed", priority: "normal", contactName: "林○菁", updatedAt: "2026-09-14" },
        { id: "DEMO-024", name: "林○樂", status: "pending-contact", priority: "normal", contactName: "林○婷", updatedAt: "2026-10-06" }
    ];

    const panel = document.querySelector(".cases-panel");
    if (!panel) return;

    const pageSize = 10;
    const statusLabels = { "pending-contact": "待聯繫", "in-progress": "進行中", completed: "已完成" };
    const state = { search: "", status: "all", page: 1 };
    const searchInput = panel.querySelector("#cases-search-input");
    const filterButtons = Array.from(panel.querySelectorAll(".cases-filter"));
    const caseList = panel.querySelector("#cases-list");
    const range = panel.querySelector("#cases-range");
    const pagination = panel.querySelector("#cases-pagination");

    function getFilteredCases() {
        const query = state.search.trim().toLocaleLowerCase();
        return demoCases
            .filter((item) => item.name.toLocaleLowerCase().includes(query) || item.id.toLocaleLowerCase().includes(query))
            .filter((item) => state.status === "all" || item.status === state.status)
            .sort((a, b) => {
                if (state.status !== "completed") {
                    const priorityOrder = Number(b.priority === "high") - Number(a.priority === "high");
                    if (priorityOrder !== 0) return priorityOrder;
                }
                // ISO dates compare in chronological order, without timezone conversion.
                return b.updatedAt.localeCompare(a.updatedAt);
            });
    }

    function createCell(text) {
        const cell = document.createElement("td");
        if (text !== undefined) cell.textContent = text;
        return cell;
    }

    function createCaseRow(item) {
        const row = document.createElement("tr");
        row.dataset.status = item.status;
        row.dataset.priority = item.priority;
        row.dataset.updated = item.updatedAt;

        const personCell = createCell();
        const person = document.createElement("div");
        person.className = "case-person";
        const avatar = document.createElement("span");
        avatar.className = "case-avatar";
        avatar.textContent = item.name.slice(0, 1);
        avatar.setAttribute("aria-hidden", "true");
        const name = document.createElement("strong");
        name.textContent = item.name;
        person.append(avatar, name);
        personCell.append(person);

        const statusCell = createCell();
        const statusBadge = document.createElement("span");
        statusBadge.className = `status-badge status-${item.status}`;
        statusBadge.textContent = statusLabels[item.status];
        statusCell.append(statusBadge);

        const priorityCell = createCell();
        const priority = document.createElement("span");
        const showPriority = item.priority === "high" && state.status !== "completed";
        priority.className = showPriority ? "priority-badge priority-high" : "priority-normal";
        priority.textContent = showPriority ? "優先" : "—";
        priority.setAttribute("aria-label", item.priority === "high" ? "優先個案" : "一般個案");
        priorityCell.append(priority);

        const updatedCell = createCell();
        const updated = document.createElement("time");
        updated.dateTime = item.updatedAt;
        updated.textContent = item.updatedAt.replaceAll("-", "/");
        updatedCell.append(updated);

        const actionCell = createCell();
        // Only these two Demo cases have matching detail pages; do not show another child's data.
        const detailHref = item.id === "DEMO-001" ? "case-detail.html" :
            item.id === "DEMO-024" ? "case-detail-empty.html" : null;
        const detailLink = document.createElement(detailHref ? "a" : "button");
        detailLink.className = "text-button";
        // Presentation-only routes; the future API will use one dynamic case-detail.html.
        if (detailHref) detailLink.href = detailHref;
        else {
            detailLink.type = "button";
            detailLink.disabled = true;
            const hint = document.createElement("small");
            hint.className = "cases-detail-hint";
            hint.id = item.id.toLowerCase() + "-detail-hint";
            hint.textContent = "Demo：尚未提供此個案詳細頁";
            detailLink.setAttribute("aria-describedby", hint.id);
            actionCell.append(hint);
        }
        detailLink.textContent = "查看個案 →";
        detailLink.setAttribute("aria-label", `查看個案：${item.name} ${item.id}`);
        actionCell.prepend(detailLink);

        row.append(personCell, createCell(item.id), statusCell, priorityCell, createCell(item.contactName), updatedCell, actionCell);
        return row;
    }

    function createPageButton(label, page, options = {}) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "cases-page-button";
        button.dataset.page = String(page);
        button.textContent = label;
        button.disabled = Boolean(options.disabled);
        button.setAttribute("aria-controls", "cases-list");
        button.setAttribute("aria-label", options.ariaLabel || `第 ${page} 頁`);
        if (options.current) {
            button.classList.add("is-current");
            button.setAttribute("aria-current", "page");
        }
        return button;
    }

    function renderCases() {
        const keepPaginationFocus = pagination.contains(document.activeElement);
        const filteredCases = getFilteredCases();
        const total = filteredCases.length;
        const pageCount = Math.ceil(total / pageSize);
        state.page = Math.max(1, Math.min(state.page, pageCount || 1));
        const start = (state.page - 1) * pageSize;
        const pageCases = filteredCases.slice(start, start + pageSize);
        caseList.replaceChildren();
        pagination.replaceChildren();

        filterButtons.forEach((button) => {
            const selected = button.dataset.filterStatus === state.status;
            button.classList.toggle("is-selected", selected);
            button.setAttribute("aria-pressed", String(selected));
        });

        if (total === 0) {
            const emptyRow = document.createElement("tr");
            const emptyCell = createCell("找不到符合條件的個案");
            emptyCell.colSpan = 7;
            emptyCell.className = "cases-empty";
            emptyRow.append(emptyCell);
            caseList.append(emptyRow);
            range.textContent = "共 0 筆";
            pagination.hidden = true;
            return;
        }

        pageCases.forEach((item) => caseList.append(createCaseRow(item)));
        range.textContent = `顯示第 ${start + 1}–${start + pageCases.length} 筆，共 ${total} 筆`;
        pagination.hidden = false;
        pagination.append(createPageButton("‹", state.page - 1, { disabled: state.page === 1, ariaLabel: "上一頁" }));
        for (let page = 1; page <= pageCount; page += 1) {
            pagination.append(createPageButton(String(page), page, { current: page === state.page }));
        }
        pagination.append(createPageButton("›", state.page + 1, { disabled: state.page === pageCount, ariaLabel: "下一頁" }));
        if (keepPaginationFocus) pagination.querySelector('[aria-current="page"]').focus({ preventScroll: true });
    }

    searchInput.addEventListener("input", () => {
        state.search = searchInput.value;
        state.page = 1;
        renderCases();
    });

    filterButtons.forEach((button) => {
        button.addEventListener("click", () => {
            state.status = button.dataset.filterStatus;
            state.page = 1;
            renderCases();
        });
    });

    pagination.addEventListener("click", (event) => {
        const button = event.target.closest("button[data-page]");
        if (!button || !pagination.contains(button) || button.disabled) return;
        state.page = Number(button.dataset.page);
        renderCases();
    });

    renderCases();
})();
