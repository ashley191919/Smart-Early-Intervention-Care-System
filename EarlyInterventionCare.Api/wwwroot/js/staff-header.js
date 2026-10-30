(() => {
    "use strict";
    const header = document.querySelector("[data-staff-header]");
    if (!header || header.dataset.staffHeaderInitialized === "true") return;
    const routeParams = new URLSearchParams(location.search);
    const apiMode = window.MedicalCaseMode?.mode === "api" || window.MedicalApiConfig?.caseSource === "api" ||
        routeParams.has("caseId") && !routeParams.get("caseId")?.startsWith("DEMO-") ||
        routeParams.getAll("source").includes("api");
    if (apiMode) {
        // Keep explicit API navigation from silently reopening static fixture pages.
        document.querySelectorAll('a[href="cases.html"], a[href="staff-dashboard.html"]').forEach(link => {
            link.href = link.getAttribute("href") + "?source=api";
        });
        document.querySelectorAll('a[href="case-create.html"]').forEach(link => link.remove());
    }
    const actions = header.querySelector(".header-actions");
    const bell = header.querySelector("#staff-notification-toggle");
    const user = header.querySelector("#staff-user-toggle");
    const remindersPanel = header.querySelector("#staff-notification-panel");
    const userPanel = header.querySelector("#staff-user-panel");
    if (!actions || !bell || !user || !remindersPanel || !userPanel) return;
    header.dataset.staffHeaderInitialized = "true";

    // Fixed Demo snapshot of Dashboard tasks, not a live feed.
    // Dashboard itself reads its existing DOM rows so date/status changes there are respected.
    // Detail-page in-memory edits are independent; no cross-page synchronization is claimed.
    // No persistence, cross-page synchronization, authentication or API.
    const reminders = [
        { title: "首次聯絡家長", name: "林○樂", caseId: "DEMO-024", date: "2026-10-07", status: "pending" },
        { title: "確認早療評估安排", name: "王○安", caseId: "DEMO-001", date: "2026-10-12", status: "pending" },
        { title: "再次聯絡家長", name: "陳○恩", caseId: "DEMO-002", date: "2026-10-06", status: "pending" },
        { title: "確認後續預約安排", name: "張○庭", caseId: "DEMO-004", date: "2026-10-12", status: "pending" },
        { title: "提醒家長完成表單", name: "王○安", caseId: "DEMO-001", date: "2026-10-14", status: "pending" },
        { title: "首次聯絡家長", name: "林○宇", caseId: "DEMO-003", date: "2026-10-08", status: "pending" }
    ];
    const detailRoutes = { "DEMO-001": "case-detail.html", "DEMO-024": "case-detail-empty.html" };
    const list = header.querySelector("#staff-notification-list");
    const empty = header.querySelector("#staff-notification-empty");
    const feedback = header.querySelector("#staff-header-logout-feedback");
    const count = header.querySelector("#staff-notification-count");
    const overdueCount = header.querySelector("#staff-overdue-count");

    function reminderIcon(overdue) {
        // Inline SVG uses the existing line-icon style, without external icon packages.
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("class", "header-reminder-icon");
        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        path.setAttribute("d", overdue
            ? "m12 3 10 18H2ZM12 9v5M12 17v.1"
            : "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4");
        svg.append(path);
        return svg;
    }

    function localDate(value) {
        return [value.getFullYear(), String(value.getMonth() + 1).padStart(2, "0"),
            String(value.getDate()).padStart(2, "0")].join("-");
    }
    function reminderState(item, today, horizon) {
        if (!["pending", "in-progress"].includes(item.status)) return null;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(item.date)) return null;
        const parsed = new Date(item.date + "T12:00:00");
        if (Number.isNaN(parsed.getTime()) || localDate(parsed) !== item.date) return null;
        if (item.date < today) return "已逾期";
        if (item.date === today) return "今日到期";
        return item.date <= horizon ? "即將到期" : null;
    }
    function attentionItems(items, now) {
        const today = localDate(now);
        const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 3, 12);
        const horizon = localDate(end);
        return items.map((item, order) => ({ ...item, order, reminderStatus: reminderState(item, today, horizon) }))
            .filter((item) => item.reminderStatus)
            // Oldest overdue deadline first, then today and nearest future dates; stable ties.
            .sort((a, b) => a.date.localeCompare(b.date) || a.order - b.order);
    }
    function reminderSource() {
        if (window.MedicalCaseMode?.mode === "api") return [];
        if (apiMode) return [];
        const rows = Array.from(document.querySelectorAll(".todo-panel .todo-item"));
        if (!rows.length) return reminders;
        return rows.map((row) => ({
            title: row.querySelector(".todo-details h3").textContent,
            name: row.querySelector(".case-person strong").textContent,
            caseId: row.dataset.caseId, date: row.dataset.dueDate, status: row.dataset.todoStatus
        }));
    }
    function refreshReminders() {
        renderReminders(attentionItems(reminderSource(), new Date()));
    }

    function renderReminders(items) {
        list.replaceChildren();
        items.slice(0, 5).forEach((item) => {
            const row = document.createElement("li");
            const route = detailRoutes[item.caseId];
            const content = document.createElement(route ? "a" : "div");
            content.className = "header-reminder";
            if (route) content.setAttribute("href", route);
            const overdue = item.reminderStatus === "已逾期";
            row.className = overdue ? "header-reminder-overdue" : "";
            const copy = document.createElement("div");
            copy.className = "header-reminder-copy";
            const top = document.createElement("div");
            top.className = "header-reminder-top";
            const title = document.createElement("strong");
            title.textContent = item.name + "｜" + item.title;
            content.setAttribute("aria-label", item.name + " " + item.caseId + "：" + item.title + "，" + item.reminderStatus);
            content.setAttribute("title", item.caseId);
            const meta = document.createElement("div");
            meta.className = "header-reminder-meta";
            const date = document.createElement("time");
            date.setAttribute("datetime", item.date);
            date.textContent = (overdue ? "原定 " : "預定 ") + item.date.replaceAll("-", "/");
            const status = document.createElement("span");
            status.textContent = " · " + (item.status === "in-progress" ? "進行中" : overdue ? "尚未完成" : "待處理");
            const label = document.createElement("span");
            label.className = "header-reminder-state";
            label.textContent = item.reminderStatus;
            top.append(title, label);
            meta.append(date, status);
            copy.append(top, meta);
            if (!route) {
                const hint = document.createElement("small");
                hint.textContent = "尚未提供此個案詳細 Demo";
                copy.append(hint);
            }
            content.append(reminderIcon(overdue), copy);
            row.append(content);
            list.append(row);
        });
        empty.hidden = items.length > 0;
        count.textContent = String(items.length);
        count.hidden = items.length === 0;
        bell.dataset.reminderCount = String(items.length);
        const overdueTotal = items.filter((item) => item.reminderStatus === "已逾期").length;
        overdueCount.textContent = overdueTotal + " 筆逾期";
        overdueCount.hidden = overdueTotal === 0;
    }

    let active = null;
    function closePanels(restoreFocus = false) {
        const previous = active;
        remindersPanel.hidden = true;
        userPanel.hidden = true;
        bell.setAttribute("aria-expanded", "false");
        user.setAttribute("aria-expanded", "false");
        bell.setAttribute("aria-label", "開啟提醒清單，共 " + bell.dataset.reminderCount + " 項待處理提醒");
        user.setAttribute("aria-label", "開啟使用者選單：醫療人員，個案管理師");
        active = null;
        if (restoreFocus && previous) previous.button.focus();
    }
    function openPanel(button, panel, focusContent = false) {
        // Re-evaluate local dates each time the bell opens (including after midnight).
        if (button === bell) refreshReminders();
        closePanels();
        panel.hidden = false;
        button.setAttribute("aria-expanded", "true");
        button.setAttribute("aria-label", button === bell ? "關閉提醒清單，共 " + bell.dataset.reminderCount + " 項待處理提醒" : "關閉使用者選單：醫療人員，個案管理師");
        feedback.hidden = true;
        active = { button, panel };
        if (focusContent) (panel.querySelector("a[href], button:not([disabled])") || panel).focus();
    }
    [[bell, remindersPanel], [user, userPanel]].forEach(([button, panel]) => {
        button.addEventListener("click", () => {
            if (active && active.button === button) closePanels();
            else openPanel(button, panel);
        });
        button.addEventListener("keydown", (event) => {
            if (event.key === "ArrowDown") {
                event.preventDefault();
                openPanel(button, panel, true);
            }
        });
    });
    document.addEventListener("click", (event) => {
        if (active && !actions.contains(event.target)) closePanels();
    });
    document.addEventListener("focusin", (event) => {
        if (active && !actions.contains(event.target)) closePanels();
    });
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && active) {
            event.preventDefault();
            closePanels(true);
        }
    });
    header.querySelector("#staff-header-logout").addEventListener("click", async () => {
        if (!window.StaffAuth?.logout) {
            feedback.textContent = "登入狀態尚未準備完成，請稍後再試。";
            feedback.hidden = false;
            return;
        }

        feedback.hidden = true;
        try {
            await window.StaffAuth.logout();
        } catch (error) {
            feedback.textContent = error instanceof Error ? error.message : "登出失敗，請稍後再試。";
            feedback.hidden = false;
        }
    });
    refreshReminders();
    closePanels();
})();
