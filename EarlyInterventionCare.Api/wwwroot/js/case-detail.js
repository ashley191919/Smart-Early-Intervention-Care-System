(() => {
    "use strict";

    // Front-end Demo only: HTML holds static, masked case and form data.
    // Case status and form status are separate; tabs do not alter either.
    // case-detail.html 與 case-detail-empty.html 是專題展示用的兩種 Demo 狀態。
    // 未來串接 API 後應整合為同一個 case-detail.html，不保留兩個正式詳細頁：
    // forms.length === 0 -> 尚未指派表單 Empty State；> 0 -> 實際表單列表。
    // completedForms.length === 0 -> 目前尚無填答結果；> 0 -> 填答結果列表。
    // 以上僅為未來整合說明，本檔目前只負責 Tabs。
    const tablist = document.querySelector(".detail-tabs");
    if (!tablist) return;

    const tabs = Array.from(tablist.querySelectorAll('[role="tab"]'));
    const panels = Array.from(document.querySelectorAll(".detail-tab-panel"));

    function activateTab(target, moveFocus = false) {
        tabs.forEach((tab) => {
            const selected = tab === target;
            tab.classList.toggle("is-active", selected);
            tab.setAttribute("aria-selected", String(selected));
            tab.tabIndex = selected ? 0 : -1;
        });
        panels.forEach((panel) => {
            panel.hidden = panel.id !== target.getAttribute("aria-controls");
        });
        if (moveFocus) target.focus({ preventScroll: true });
    }

    tabs.forEach((tab, index) => {
        tab.addEventListener("click", () => activateTab(tab));
        tab.addEventListener("keydown", (event) => {
            let nextIndex;
            if (event.key === "ArrowRight") nextIndex = (index + 1) % tabs.length;
            else if (event.key === "ArrowLeft") nextIndex = (index - 1 + tabs.length) % tabs.length;
            else if (event.key === "Home") nextIndex = 0;
            else if (event.key === "End") nextIndex = tabs.length - 1;
            else return;

            event.preventDefault();
            activateTab(tabs[nextIndex], true);
        });
    });

    activateTab(tabs[0]);
})();
