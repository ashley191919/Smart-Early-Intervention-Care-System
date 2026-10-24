(function (root, factory) {
    const view = factory();
    if (typeof module === "object" && module.exports) module.exports = view;
    else root.MedicalTaskView = view;
})(typeof window === "undefined" ? globalThis : window, function () {
    "use strict";
    function mount(container, summary, retry, document = container.ownerDocument) {
        const node = (tag, text, className) => {
            const element = document.createElement(tag);
            if (text !== undefined) element.textContent = String(text);
            if (className) element.className = className;
            return element;
        };
        function notice(parent, message, error) {
            const text = node("p", message); text.setAttribute("role", "status"); parent.append(text);
            if (error) {
                const button = node("button", "重新載入", "primary-button"); button.type = "button";
                button.addEventListener("click", retry); parent.append(button);
            }
        }
        return state => {
            container.replaceChildren(); container.setAttribute("aria-busy", String(state.phase === "loading" || state.completionPhase === "loading"));
            summary.textContent = state.completionPhase === "ready" ?
                `必要已提交 ${state.completion.requiredSubmittedCount} / ${state.completion.requiredTaskCount}` :
                state.completionPhase === "error" ? "未提供" : "載入中…";
            const progress = node("section", undefined, "panel"); progress.append(node("h2", "問卷任務統計"));
            if (state.completionPhase === "ready") {
                const c = state.completion, dl = node("dl", undefined, "detail-data-grid");
                [["已指派任務數", c.totalTaskCount], ["必要任務數", c.requiredTaskCount], ["必要已提交", c.requiredSubmittedCount],
                    ["必要待填", c.requiredPendingCount], ["必要進行中", c.requiredInProgressCount],
                    ["必要任務皆已提交", c.allRequiredSubmitted ? "是" : "否"]].forEach(([label, value]) => {
                    const row = node("div"); row.append(node("dt", label), node("dd", value)); dl.append(row);
                });
                progress.append(dl, node("p", "以上為服務回傳的任務統計，不代表評估完成或個案結案。"));
            } else notice(progress, state.completionError?.message || "正在載入問卷統計…", state.completionPhase === "error");
            const records = node("section", undefined, "panel"); records.append(node("h2", "已指派表單紀錄"));
            if (state.phase !== "ready") notice(records, state.error?.message ||
                (state.phase === "empty" ? "目前尚未指派任何表單。" : "正在載入表單紀錄…"), state.phase === "error");
            else {
                const wrap = node("div", undefined, "table-wrap detail-table-wrap"); wrap.tabIndex = 0;
                wrap.setAttribute("role", "region"); wrap.setAttribute("aria-label", "已指派表單紀錄，可橫向捲動");
                const table = node("table", undefined, "detail-forms-table medical-api-task-table"), head = node("thead"), tr = node("tr"), body = node("tbody");
                ["問卷／版本", "填答角色", "狀態", "輪次", "必要性", "提交時間", "填答結果"].forEach(label => {
                    const th = node("th", label); th.setAttribute("scope", "col"); tr.append(th);
                });
                head.append(tr);
                state.rows.forEach(row => {
                    const item = node("tr");
                    [row.title + "／" + row.version, row.role, row.statusLabel, row.round, row.required, row.submittedAt, row.result]
                        .forEach(text => item.append(node("td", text)));
                    body.append(item);
                });
                table.append(head, body); wrap.append(table); records.append(wrap);
            }
            container.append(progress, records);
        };
    }
    return { mount };
});
