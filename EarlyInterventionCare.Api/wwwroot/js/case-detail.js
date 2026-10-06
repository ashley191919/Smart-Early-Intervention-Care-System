(() => {
    "use strict";

    // Front-end Demo only: HTML holds static, masked case, contact, appointment and form data.
    // Case, contact, appointment and form statuses are separate; tabs do not alter any of them.
    // 個案 status 維持 pending-contact / in-progress / completed。
    // data-contact-status：pending（待聯絡）、contacted（已聯絡）、no-answer（未接）、
    // needs-follow-up（需再次聯絡）；data-appointment-status：unscheduled / scheduled。
    // 以上與表單完成份數 / 指派總份數為不同維度，不用聯絡或預約狀態取代個案 status。
    // 未來 Dashboard 近期追蹤可整合聯絡紀錄、預約資訊及表單進度；目前不串接、不儲存。
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
