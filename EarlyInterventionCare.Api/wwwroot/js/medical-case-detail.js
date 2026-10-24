(() => {
    "use strict";
    if (window.MedicalCaseMode.mode !== "api") return;
    const main = document.querySelector(".case-detail-content");
    if (!main) return;
    const model = window.MedicalCases, route = window.MedicalCaseMode;
    const node = (tag, text, className) => {
        const element = document.createElement(tag);
        if (text !== undefined) element.textContent = text;
        if (className) element.className = className;
        return element;
    };
    function panel(title, fields) {
        const section = node("section", undefined, "panel");
        const heading = node("div", undefined, "panel-heading"); heading.append(node("h2", title)); section.append(heading);
        const dl = node("dl", undefined, "detail-data-grid");
        fields.forEach(([label, value]) => { const group = node("div"); group.append(node("dt", label), node("dd", value)); dl.append(group); });
        section.append(dl); return section;
    }
    function render(data) {
        main.replaceChildren(); main.setAttribute("aria-busy", String(data.phase === "loading"));
        const back = node("nav", undefined, "detail-back-nav"), link = node("a", "← 返回個案管理");
        link.href = "cases.html?source=api"; back.append(link); main.append(back);
        const heading = node("section", undefined, "page-heading detail-heading");
        const copy = node("div"); copy.append(node("p", "MEDICAL WORKSPACE", "eyebrow"), node("h1", "個案詳細資料")); heading.append(copy); main.append(heading);
        if (data.phase !== "ready" || !data.detail) {
            const status = node("section", undefined, "panel");
            const message = node("p", data.error?.message || "正在載入個案…"); message.setAttribute("role", "status"); status.append(message);
            if (data.phase === "error") {
                const retry = node("button", "重新載入", "primary-button"); retry.type = "button";
                retry.addEventListener("click", () => {
                    if (!window.StaffAuth?.getCurrentUser()) void window.StaffAuth.requireStaffSession();
                    else load();
                }); status.append(retry);
            }
            main.append(status); return;
        }
        const value = data.detail;
        const identity = node("div", undefined, "detail-identity");
        const person = node("div", undefined, "case-person"), text = node("span");
        text.append(node("strong", value.name), node("small", "個案編號：" + value.code));
        person.append(node("span", value.name.slice(0, 1), "case-avatar"), text);
        identity.append(person, node("span", value.statusLabel, "status-badge status-" + value.statusClass)); copy.append(identity);
        const summary = node("section", undefined, "summary-grid detail-summary-grid");
        [["處理狀態", value.statusLabel], ["表單進度", model.missing], ["最近更新", new Date(value.updatedAt).toLocaleString("zh-TW", { timeZone: "Asia/Taipei" })]].forEach(([label, content]) => {
            const card = node("article", undefined, "summary-card detail-summary-card"), body = node("div");
            body.append(node("p", label), node("strong", content)); card.append(body); summary.append(card);
        });
        main.append(summary);
        const tabs = node("div", undefined, "detail-tabs"); tabs.setAttribute("role", "tablist"); tabs.setAttribute("aria-label", "個案詳細資料頁籤");
        const panels = [], buttons = [];
        function activate(index, focus = false) {
            panels.forEach((p, i) => { p.hidden = i !== index; });
            buttons.forEach((b, i) => { b.setAttribute("aria-selected", String(i === index)); b.tabIndex = i === index ? 0 : -1; b.classList.toggle("is-active", i === index); });
            if (focus) buttons[index].focus();
        }
        ["基本資料", "追蹤紀錄", "表單紀錄"].forEach((title, index) => {
            const button = node("button", title, "detail-tab"); button.type = "button"; button.id = "case-api-tab-" + index;
            button.setAttribute("role", "tab"); button.setAttribute("aria-controls", "case-api-panel-" + index);
            const content = node("section", undefined, "detail-tab-panel"); content.id = "case-api-panel-" + index; content.tabIndex = 0;
            content.setAttribute("role", "tabpanel"); content.setAttribute("aria-labelledby", button.id);
            button.addEventListener("click", () => activate(index));
            button.addEventListener("keydown", event => {
                const next = { ArrowRight: (index + 1) % 3, ArrowLeft: (index + 2) % 3, Home: 0, End: 2 }[event.key];
                if (next !== undefined) { event.preventDefault(); activate(next, true); }
            });
            buttons.push(button); panels.push(content); tabs.append(button);
        });
        const basic = node("div", undefined, "detail-basic-grid");
        basic.append(panel("兒童基本資料", [["姓名", value.name], ["性別", value.sex], ["出生日期", value.birthDate], ["身分證字號", value.nationalId], ["聯絡地址", value.address]]),
            panel("家長／主要聯絡人", [["聯絡人姓名", value.contactName], ["與兒童關係", value.relationship], ["手機號碼", value.phone], ["電子郵件", value.email]]));
        panels[0].append(basic); panels[1].append(panel("追蹤摘要", [["聯絡紀錄", model.missing], ["預約資訊", model.missing], ["後續待辦", model.missing]]));
        panels[2].append(panel("表單紀錄", [["表單進度", model.missing], ["填答結果", model.missing]]));
        main.append(tabs, ...panels); activate(0);
    }
    const store = model.createStore(model.createApiAdapter(window.MedicalApi.createClient()), {
        onChange: render, canRead: () => Boolean(window.StaffAuth?.getCurrentUser())
    });
    function load() {
        if (route.invalid || !window.MedicalApi.uuid(route.caseId)) store.fail({ status: 404 });
        else void store.detail(route.caseId);
    }
    render(store.snapshot());
    if (window.StaffAuth?.getCurrentUser()) load();
    document.addEventListener("staff-authenticated", load);
    document.addEventListener("staff-permissions-changed", () => { if (!window.StaffAuth?.getCurrentUser()) store.clearData(); });
    window.addEventListener("pagehide", () => store.invalidate());
})();
