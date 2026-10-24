(() => {
    "use strict";
    const panel = document.querySelector(".cases-panel");
    if (!panel) return;
    const model = window.MedicalCases, route = window.MedicalCaseMode;
    const apiMode = route.mode === "api", pageSize = 10;
    const state = { search: "", status: "all", sort: "default", page: 1 };
    const search = panel.querySelector("#cases-search-input");
    const filters = panel.querySelector(".cases-filters");
    const sort = panel.querySelector("#cases-sort");
    const list = panel.querySelector("#cases-list");
    const range = panel.querySelector("#cases-range");
    const pagination = panel.querySelector("#cases-pagination");
    const feedback = panel.querySelector("#cases-feedback");
    let store;
    const node = (tag, text, className) => {
        const element = document.createElement(tag);
        if (text !== undefined) element.textContent = text;
        if (className) element.className = className;
        return element;
    };
    const labels = apiMode ? { all: "全部", ...model.labels } :
        { all: "全部", "pending-contact": "待聯繫", "in-progress": "進行中", completed: "已完成" };
    filters.replaceChildren();
    for (const [value, label] of Object.entries(labels)) {
        const button = node("button", label, "cases-filter"); button.type = "button";
        button.dataset.filterStatus = value; button.setAttribute("aria-controls", "cases-list");
        button.addEventListener("click", () => { state.status = value; state.page = 1; render(); });
        filters.append(button);
    }
    if (apiMode) {
        search.placeholder = "搜尋本頁姓名或個案編號";
        panel.querySelector('label[for="cases-search-input"]').textContent = search.placeholder;
        panel.querySelector("#cases-scope").hidden = false;
        sort.options[0].textContent = "最近更新（新到舊）";
        sort.querySelector('option[value="newest"]').remove();
    }
    function cell(text) { return node("td", text); }
    function row(item) {
        const tr = node("tr"); tr.dataset.caseId = item.id;
        const person = node("div", undefined, "case-person");
        const avatar = node("span", item.name.slice(0, 1), "case-avatar"); avatar.setAttribute("aria-hidden", "true");
        person.append(avatar, node("strong", item.name));
        const first = cell(); first.append(person);
        const status = cell(); status.append(node("span", item.statusLabel || labels[item.status], "status-badge status-" + (item.statusClass || item.status)));
        const priority = cell();
        const showPriority = !apiMode && item.priority === "high" && state.status !== "completed";
        priority.append(node("span", apiMode ? model.missing : showPriority ? "優先" : "—",
            showPriority ? "priority-badge priority-high" : "priority-normal"));
        const updated = cell();
        const time = node("time", apiMode ? new Date(item.updatedAt).toLocaleDateString("zh-TW", { timeZone: "Asia/Taipei" }) : item.updatedAt.replaceAll("-", "/"));
        time.dateTime = item.updatedAt; updated.append(time);
        const actions = cell();
        const href = apiMode ? item.detailHref : item.id === "DEMO-001" ? "case-detail.html" : item.id === "DEMO-024" ? "case-detail-empty.html" : null;
        const link = node(href ? "a" : "button", "查看個案 →", "text-button");
        if (href) link.href = href;
        else { link.type = "button"; link.disabled = true; actions.append(node("small", "尚未提供此個案詳細資料", "cases-detail-hint")); }
        link.setAttribute("aria-label", "查看個案：" + item.name + " " + (item.code || item.id)); actions.prepend(link);
        tr.append(first, cell(item.code || item.id), status, priority, cell(item.contactName), updated, actions);
        Array.from(tr.children).forEach((td, index) => { td.dataset.label = ["個案", "個案編號", "處理狀態", "優先程度", "聯絡人", "最近更新", "操作"][index]; });
        return tr;
    }
    function pageButton(label, page, disabled, current = false) {
        const button = node("button", label, "cases-page-button"); button.type = "button"; button.disabled = disabled;
        button.setAttribute("aria-label", label === "‹" ? "上一頁" : label === "›" ? "下一頁" : "第 " + page + " 頁");
        if (current) { button.setAttribute("aria-current", "page"); button.classList.add("is-current"); }
        button.addEventListener("click", () => {
            if (apiMode) void store.list(page, pageSize);
            else { state.page = page; render(); }
        });
        return button;
    }
    function render() {
        const data = store?.snapshot() || { phase: "loading", rows: [], page: 1 };
        list.replaceChildren(); pagination.replaceChildren(); range.textContent = ""; feedback.textContent = "";
        panel.setAttribute("aria-busy", String(data.phase === "loading"));
        Array.from(filters.children).forEach(button => {
            const selected = button.dataset.filterStatus === state.status;
            button.classList.toggle("is-selected", selected); button.setAttribute("aria-pressed", String(selected));
        });
        if (data.phase === "loading" || data.phase === "error") {
            const message = data.phase === "error" ? data.error.message : "正在載入個案…";
            const tr = node("tr"), td = cell(message); td.colSpan = 7; td.className = "cases-empty"; tr.append(td); list.append(tr);
            feedback.textContent = data.phase === "error" ? message : "";
            pagination.hidden = true; return;
        }
        const rows = model.selectRows(data.rows, { ...state, mode: route.mode });
        const pageCount = Math.max(1, Math.ceil(rows.length / pageSize));
        state.page = Math.min(state.page, pageCount);
        const start = apiMode ? 0 : (state.page - 1) * pageSize;
        const visible = apiMode ? rows : rows.slice(start, start + pageSize);
        if (!visible.length) {
            const tr = node("tr"), td = cell(data.rows.length ? "本頁找不到符合條件的個案" : "目前沒有可顯示的個案");
            td.colSpan = 7; td.className = "cases-empty"; tr.append(td); list.append(tr);
        } else visible.forEach(item => list.append(row(item)));
        range.textContent = apiMode ? "第 " + data.page + " 頁 · 本頁取得 " + data.rows.length + " 筆，篩選後 " + rows.length + " 筆" :
            rows.length ? "顯示第 " + (start + 1) + "–" + (start + visible.length) + " 筆，共 " + rows.length + " 筆" : "共 0 筆";
        pagination.hidden = false;
        const current = apiMode ? data.page : state.page;
        pagination.append(pageButton("‹", current - 1, current === 1));
        if (apiMode) pagination.append(pageButton(String(current), current, true, true));
        else for (let page = 1; page <= pageCount; page++) pagination.append(pageButton(String(page), page, false, page === current));
        pagination.append(pageButton("›", current + 1, apiMode ? !data.hasNext : current === pageCount));
    }
    search.addEventListener("input", () => { state.search = search.value; state.page = 1; render(); });
    sort.addEventListener("change", () => { state.sort = sort.value; state.page = 1; render(); });
    panel.querySelector("#cases-reload").addEventListener("click", () => {
        if (apiMode && !window.StaffAuth?.getCurrentUser()) void window.StaffAuth.requireStaffSession();
        else if (store) void store.list(apiMode ? store.snapshot().page : 1, pageSize);
    });
    function start(adapter) {
        store = model.createStore(adapter, { onChange: render, canRead: () => !apiMode || Boolean(window.StaffAuth?.getCurrentUser()) });
        if (route.invalid) { store.fail({ status: 404 }); return; }
        if (!apiMode || window.StaffAuth?.getCurrentUser()) void store.list();
        else render();
    }
    if (apiMode) start(model.createApiAdapter(window.MedicalApi.createClient()));
    else {
        // Demo data is loaded only on this explicit path, never as an API error fallback.
        const script = document.createElement("script"); script.src = "js/case-demo-data.js?v=20261024-case-read1";
        script.onload = () => start(model.createDemoAdapter(window.MedicalCaseDemo));
        script.onerror = () => { start(model.createDemoAdapter([])); store.fail({ status: 503 }); };
        document.head.append(script); render();
    }
    document.addEventListener("staff-authenticated", () => { if (apiMode && store && !route.invalid) void store.list(store.snapshot().page); });
    document.addEventListener("staff-permissions-changed", () => {
        if (apiMode && store && !window.StaffAuth?.getCurrentUser()) store.clearData();
    });
    window.addEventListener("pagehide", () => store?.invalidate());
    window.addEventListener("pageshow", event => { if (event.persisted && !apiMode && store) void store.list(); });
})();
