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
    // 表單紀錄整合結果入口：僅已完成的獨立填答紀錄提供查看結果連結，不另設結果 Tab。
    // 同一表單的 parent / teacher 分別對應不同 Response，不共用答案或合併結果。
    // 以上僅為未來整合說明；本檔負責 Tabs 與記憶體內的聯絡紀錄 Demo。
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

    const modal = document.getElementById("contact-modal");
    const form = document.getElementById("contact-form");
    const openButton = document.getElementById("add-contact-record");
    const timeline = document.getElementById("detail-contact-timeline");
    const summary = document.getElementById("detail-contact-summary");
    if (!modal || !form || !openButton || !timeline || !summary) return;

    const emptyState = document.getElementById("detail-contact-empty");
    const success = document.getElementById("contact-success");
    const noteCount = document.getElementById("contactNote-count");
    const fieldIds = ["contactDate", "contactTime", "contactMethod", "contactTarget", "contactResult", "contactNote"];
    const fields = fieldIds.map((id) => document.getElementById(id));
    const requiredMessages = {
        contactDate: "請選擇聯絡日期",
        contactTime: "請選擇聯絡時間",
        contactMethod: "請選擇聯絡方式",
        contactTarget: "請選擇聯絡對象",
        contactResult: "請選擇聯絡結果",
        contactNote: "請輸入聯絡紀錄"
    };
    const methods = { phone: "電話", sms: "簡訊", other: "其他" };
    const targets = { "primary-contact": "主要聯絡人", "other-family": "其他家屬" };
    const results = { contacted: "已聯絡", "no-answer": "未接", "needs-follow-up": "需再次聯絡" };
    const optionSets = { contactMethod: methods, contactTarget: targets, contactResult: results };
    const keyForLabel = (options, label) => Object.keys(options).find((key) => options[key] === label);

    // Keep the existing HTML Demo records; all additions are in memory and vanish on reload.
    // Future ContactRecord: contactRecordId, caseId, contactDate, contactTime, contactMethod,
    // contactTarget, contactResult, note, createdBy, createdAt. No API or persistence here.
    const records = Array.from(timeline.querySelectorAll(".detail-contact-item")).map((item, index) => {
        const [contactDate, contactTime] = item.querySelector("time").getAttribute("datetime").split("T");
        const metadata = item.querySelectorAll(".detail-contact-meta dd");
        return {
            id: index + 1, contactDate, contactTime,
            contactMethod: keyForLabel(methods, metadata[0].textContent.trim()),
            contactTarget: keyForLabel(targets, metadata[1].textContent.trim()),
            contactResult: item.dataset.contactStatus,
            note: item.querySelector(".detail-contact-note").textContent
        };
    });
    let nextRecordId = records.length + 1;

    function setFieldError(field, message = "") {
        const error = document.getElementById(field.id + "-error");
        field.setAttribute("aria-invalid", String(Boolean(message)));
        error.textContent = message;
        error.hidden = !message;
    }

    function fieldError(field) {
        const value = field.value.trim();
        if (!value) return requiredMessages[field.id];
        if (optionSets[field.id] && !Object.hasOwn(optionSets[field.id], value)) return requiredMessages[field.id];
        if (field.id === "contactDate" && (!/^\d{4}-\d{2}-\d{2}$/.test(value) || !field.validity.valid)) return "請選擇有效的聯絡日期";
        if (field.id === "contactTime" && (!/^([01]\d|2[0-3]):[0-5]\d$/.test(value) || !field.validity.valid)) return "請選擇有效的聯絡時間";
        if (field.id === "contactNote" && value.length > 500) return "聯絡紀錄不可超過 500 字";
        return "";
    }

    function resetContactForm() {
        form.reset();
        fields.forEach((field) => setFieldError(field));
        noteCount.textContent = "0 / 500";
    }

    function openContactModal() {
        resetContactForm();
        modal.showModal(); // Native dialog contains keyboard focus and makes the background inert.
        fields[0].focus({ preventScroll: true });
    }

    function closeContactModal() {
        modal.close();
    }

    // Handles every closing path, including the native Escape/cancel interaction.
    modal.addEventListener("close", () => {
        resetContactForm();
        openButton.focus({ preventScroll: true });
    });
    modal.addEventListener("cancel", (event) => {
        event.preventDefault();
        closeContactModal();
    });
    modal.addEventListener("click", (event) => {
        if (event.target !== modal) return;
        const bounds = modal.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom) closeContactModal();
    });
    openButton.addEventListener("click", openContactModal);
    document.getElementById("contact-modal-close").addEventListener("click", closeContactModal);
    document.getElementById("contact-modal-cancel").addEventListener("click", closeContactModal);

    fields.forEach((field) => {
        const handleCorrection = () => {
            if (field.id === "contactNote") {
                field.value = field.value.slice(0, 500);
                noteCount.textContent = field.value.length + " / 500";
            }
            if (field.getAttribute("aria-invalid") === "true") setFieldError(field, fieldError(field));
        };
        field.addEventListener("input", handleCorrection);
        field.addEventListener("change", handleCorrection);
    });

    function validateContactForm() {
        let firstInvalid = null;
        fields.forEach((field) => {
            const message = fieldError(field);
            setFieldError(field, message);
            if (message && !firstInvalid) firstInvalid = field;
        });
        if (firstInvalid) firstInvalid.focus({ preventScroll: true });
        return !firstInvalid;
    }

    function createContactRecord() {
        return {
            id: nextRecordId++,
            contactDate: fields[0].value,
            contactTime: fields[1].value,
            contactMethod: fields[2].value,
            contactTarget: fields[3].value,
            contactResult: fields[4].value,
            note: fields[5].value.trim()
        };
    }

    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function renderContactRecords() {
        // ISO local date/time strings sort chronologically; no Date parsing/timezone conversion.
        const timestamp = (record) => record.contactDate + "T" + record.contactTime;
        records.sort((a, b) => timestamp(b).localeCompare(timestamp(a)) || b.id - a.id);
        const fragment = document.createDocumentFragment();
        records.forEach((record) => {
            const item = element("li", "detail-contact-item");
            item.dataset.contactStatus = record.contactResult;
            const article = element("article");
            const heading = element("h3");
            heading.id = "detail-contact-record-time-" + record.id;
            article.setAttribute("aria-labelledby", heading.id);
            const time = element("time", "", record.contactDate.replace(/-/g, "/") + " " + record.contactTime);
            time.setAttribute("datetime", timestamp(record));
            heading.append(time);
            const metadata = element("dl", "detail-contact-meta");
            [
                ["聯絡方式", methods[record.contactMethod]],
                ["聯絡結果", results[record.contactResult]],
                ["聯絡對象", targets[record.contactTarget]]
            ].forEach(([label, value]) => {
                const group = element("div");
                const description = element("dd");
                if (label === "聯絡結果") {
                    const badge = element("span", "detail-tracking-badge", value);
                    badge.classList.toggle("is-neutral", record.contactResult !== "contacted");
                    description.append(badge);
                } else description.textContent = value;
                group.append(element("dt", "", label), description);
                metadata.append(group);
            });
            // User notes are text nodes, never interpreted as HTML.
            article.append(heading, metadata, element("p", "detail-contact-note", record.note));
            item.append(article);
            fragment.append(item);
        });
        timeline.replaceChildren(fragment);
        timeline.hidden = records.length === 0;
        if (emptyState) emptyState.hidden = records.length > 0;
    }

    function updateContactSummary() {
        const latest = records[0];
        if (!latest) return;
        summary.dataset.contactStatus = latest.contactResult;
        const badge = summary.querySelector(".detail-tracking-badge");
        badge.textContent = results[latest.contactResult];
        badge.classList.toggle("is-neutral", latest.contactResult !== "contacted");
        // Only contact progress changes: never mutate case status, appointment or form progress.
    }

    form.addEventListener("submit", (event) => {
        event.preventDefault();
        if (!validateContactForm()) return;
        records.push(createContactRecord());
        renderContactRecords();
        updateContactSummary();
        closeContactModal();
        success.textContent = "聯絡紀錄已新增（Demo，重新整理後不保留）";
        success.hidden = false;
    });
})();


// Appointment Demo is isolated per document/case; it does not share or persist data across pages.
(() => {
    "use strict";
    const panel = document.getElementById("detail-appointment-panel");
    if (!panel) return;
    const summary = document.querySelector(".detail-tracking-summary-grid [data-appointment-status]");
    const modal = document.getElementById("appointment-modal");
    const form = document.getElementById("appointment-form");
    const cancelModal = document.getElementById("appointment-cancel-modal");
    const addButton = document.getElementById("add-appointment");
    const editButton = document.getElementById("edit-appointment");
    const cancelButton = document.getElementById("cancel-appointment");
    const info = document.getElementById("appointment-info");
    const empty = document.getElementById("appointment-empty");
    const feedback = document.getElementById("appointment-feedback");
    const inactiveNote = document.getElementById("appointment-inactive-note");
    const historyList = document.getElementById("appointment-history-list");
    const historyEmpty = document.getElementById("appointment-history-empty");
    const dateField = document.getElementById("assessmentDate");
    const timeField = document.getElementById("assessmentTime");
    const typeField = document.getElementById("assessmentType");
    const noteField = document.getElementById("appointmentNote");
    const fields = [dateField, timeField, typeField, noteField];
    const types = { initial: "初次評估", "follow-up": "追蹤評估", other: "其他", "early-intervention": "早療評估" };
    const statuses = { scheduled: "已預約", cancelled: "已取消", unscheduled: "尚未預約" };
    const actions = { existing: "既有預約", create: "新增預約", edit: "修改預約", cancel: "取消預約" };
    const state = { caseId: panel.dataset.caseId, current: null, history: [] };
    let nextId = 1;
    let editing = false;
    let modalTrigger = null;

    // Read only this page's existing seed. Never validate/hide historical appointments on load.
    const seedDate = document.getElementById("appointment-date");
    if (seedDate) {
        state.current = {
            id: nextId++, caseId: state.caseId,
            date: seedDate.getAttribute("datetime"),
            time: document.getElementById("appointment-time").textContent.trim(),
            type: document.getElementById("appointment-type").dataset.evaluationType,
            note: "", status: "scheduled"
        };
        // Original Demo has no creation timestamp: do not invent one.
        state.history.push({ action: "existing", operatedAt: null, appointment: { ...state.current } });
    }

    function localDateTime() {
        const now = new Date();
        const pad = (number) => String(number).padStart(2, "0");
        const date = String(now.getFullYear()).padStart(4, "0") + "-" + pad(now.getMonth() + 1) + "-" + pad(now.getDate());
        return { date, timestamp: date + "T" + pad(now.getHours()) + ":" + pad(now.getMinutes()) + ":" + pad(now.getSeconds()) };
    }

    function validCalendarDate(value) {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
        if (!match) return false;
        const [, yearText, monthText, dayText] = match;
        const year = Number(yearText), month = Number(monthText), day = Number(dayText);
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        return year > 0 && month >= 1 && month <= 12 && day >= 1 && day <= days[month - 1];
    }

    function setError(field, message = "") {
        const error = document.getElementById(field.id + "-error");
        field.setAttribute("aria-invalid", String(Boolean(message)));
        error.textContent = message;
        error.hidden = !message;
    }

    function validationMessage(field) {
        const value = field.value.trim();
        if (field === dateField) {
            if (!value) return "請選擇評估日期";
            if (!validCalendarDate(value)) return "請輸入有效的評估日期";
            if (value < localDateTime().date) return "預約日期不得早於今天";
        } else if (field === timeField) {
            if (!value) return "請選擇評估時間";
            if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(value) || !field.validity.valid) return "請輸入有效的評估時間";
        } else if (field === typeField) {
            if (!Object.hasOwn(types, value)) return "請選擇評估類型";
        } else if (value.length > 500) return "備註不可超過 500 字";
        return "";
    }

    function resetAppointmentForm() {
        form.reset();
        fields.forEach((field) => setError(field));
    }

    function updateDateLimit() {
        dateField.min = localDateTime().date;
    }

    function hasActiveAppointment() {
        return state.current?.status === "scheduled";
    }

    function returnFocus(trigger) {
        const target = trigger && !trigger.hidden ? trigger : hasActiveAppointment() ? editButton : addButton;
        target.focus({ preventScroll: true });
    }

    function openAppointmentModal(isEdit, trigger) {
        if (isEdit && !hasActiveAppointment()) return;
        editing = isEdit;
        modalTrigger = trigger;
        resetAppointmentForm();
        updateDateLimit();
        document.getElementById("appointment-modal-title").textContent = isEdit ? "編輯預約" : "新增預約";
        if (isEdit) {
            dateField.value = state.current.date;
            timeField.value = state.current.time;
            typeField.value = state.current.type;
            noteField.value = state.current.note;
        }
        feedback.hidden = true;
        modal.showModal();
        dateField.focus({ preventScroll: true });
    }

    function bindDialog(dialog, dismissButtons, afterClose) {
        dismissButtons.forEach((button) => button.addEventListener("click", () => dialog.close()));
        dialog.addEventListener("cancel", (event) => {
            event.preventDefault();
            dialog.close();
        });
        dialog.addEventListener("close", afterClose);
        dialog.addEventListener("click", (event) => {
            if (event.target !== dialog) return;
            const bounds = dialog.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right ||
                event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
        });
    }

    bindDialog(modal, [
        document.getElementById("appointment-modal-dismiss"),
        document.getElementById("appointment-modal-close")
    ], () => {
        resetAppointmentForm();
        returnFocus(modalTrigger);
    });
    bindDialog(cancelModal, [
        document.getElementById("appointment-cancel-dismiss"),
        document.getElementById("appointment-cancel-close")
    ], () => returnFocus(cancelButton));

    addButton.addEventListener("click", () => openAppointmentModal(false, addButton));
    editButton.addEventListener("click", () => openAppointmentModal(true, editButton));
    dateField.addEventListener("focus", updateDateLimit);
    fields.forEach((field) => {
        const correctField = () => {
            if (field === noteField) field.value = field.value.slice(0, 500);
            if (field.getAttribute("aria-invalid") === "true") setError(field, validationMessage(field));
        };
        field.addEventListener("input", correctField);
        field.addEventListener("change", correctField);
    });

    function makeNode(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function appointmentText(appointment) {
        return appointment.date.replaceAll("-", "/") + " " + appointment.time + "｜" + types[appointment.type];
    }

    function addHistory(action, before) {
        state.history.unshift({
            action, operatedAt: localDateTime().timestamp,
            appointment: { ...state.current },
            before: before ? { ...before } : null
        });
    }

    function renderHistory() {
        const fragment = document.createDocumentFragment();
        state.history.forEach((record) => {
            const item = makeNode("li", "appointment-history-item");
            item.dataset.appointmentAction = record.action;
            item.append(makeNode("h4", "", actions[record.action] + "（Demo）"));
            if (record.operatedAt) {
                const time = makeNode("time", "", "操作時間：" + record.operatedAt.replace("T", " ").replaceAll("-", "/"));
                time.setAttribute("datetime", record.operatedAt);
                item.append(time);
            } else item.append(makeNode("p", "", "操作時間：未提供（原始 Demo）"));
            item.append(makeNode("p", "", "評估：" + appointmentText(record.appointment) + "｜" + statuses[record.appointment.status]));
            item.append(makeNode("p", "", "備註：" + (record.appointment.note || "—")));
            if (record.before) {
                item.append(makeNode("p", "", "修改前：" + appointmentText(record.before) + "；備註：" + (record.before.note || "—")));
            }
            fragment.append(item);
        });
        historyList.replaceChildren(fragment);
        historyEmpty.hidden = state.history.length > 0;
    }

    function renderAppointment() {
        const current = state.current;
        const active = hasActiveAppointment();
        const status = current ? current.status : "unscheduled";
        panel.dataset.appointmentStatus = status;
        summary.dataset.appointmentStatus = status;
        const summaryBadge = summary.querySelector(".detail-tracking-badge");
        summaryBadge.textContent = statuses[status];
        summaryBadge.classList.toggle("is-neutral", !active);
        addButton.hidden = active;
        editButton.hidden = !active;
        cancelButton.hidden = !active;
        empty.hidden = Boolean(current);
        info.hidden = !current;
        inactiveNote.hidden = status !== "cancelled";
        info.replaceChildren();
        if (current) {
            [
                ["預約狀態", statuses[status], "status"],
                ["評估日期", current.date.replaceAll("-", "/"), "date"],
                ["評估時間", current.time, "time"],
                ["評估類型", types[current.type], "type"],
                ["備註", current.note || "—", "note"]
            ].forEach(([label, value, key]) => {
                const group = makeNode("div", key === "note" ? "detail-data-wide" : "");
                const description = makeNode("dd");
                let content;
                if (key === "status") {
                    content = makeNode("span", "detail-tracking-badge", value);
                    content.classList.toggle("is-neutral", !active);
                } else if (key === "date" || key === "time") {
                    content = makeNode("time", "", value);
                    content.setAttribute("datetime", key === "date" ? current.date : current.date + "T" + current.time);
                } else content = makeNode("span", "", value);
                content.id = "appointment-" + key;
                if (key === "type") content.dataset.evaluationType = current.type;
                description.append(content);
                group.append(makeNode("dt", "", label), description);
                info.append(group);
            });
        }
        renderHistory();
        // Only appointment progress changes. No case/contact/form state or Dashboard changes.
    }

    form.addEventListener("submit", (event) => {
        event.preventDefault();
        updateDateLimit();
        let firstInvalid = null;
        fields.forEach((field) => {
            const message = validationMessage(field);
            setError(field, message);
            if (message && !firstInvalid) firstInvalid = field;
        });
        if (firstInvalid) {
            firstInvalid.focus({ preventScroll: true });
            return;
        }
        const before = editing ? { ...state.current } : null;
        state.current = {
            id: editing ? before.id : nextId++, caseId: state.caseId,
            date: dateField.value, time: timeField.value, type: typeField.value,
            note: noteField.value.trim(), status: "scheduled"
        };
        addHistory(editing ? "edit" : "create", before);
        renderAppointment();
        modal.close();
        feedback.textContent = (editing ? "預約已更新" : "預約已新增") + "（Demo，重新整理後不保留）";
        feedback.hidden = false;
    });

    cancelButton.addEventListener("click", () => {
        if (!hasActiveAppointment()) return;
        document.getElementById("appointment-cancel-summary").textContent = "本次預約：" + appointmentText(state.current);
        cancelModal.showModal();
        // Focus the non-destructive choice first; confirmation is explicit.
        document.getElementById("appointment-cancel-dismiss").focus({ preventScroll: true });
    });
    document.getElementById("appointment-cancel-confirm").addEventListener("click", () => {
        if (!hasActiveAppointment()) {
            cancelModal.close();
            return;
        }
        state.current = { ...state.current, status: "cancelled" };
        addHistory("cancel");
        renderAppointment();
        cancelModal.close();
        feedback.textContent = "預約已取消，紀錄已保留（Demo）";
        feedback.hidden = false;
    });

    // Front-end memory only. Future appointment API should revalidate dates/types server-side,
    // keep edit/cancellation audit records, and authorize updates to the relevant case.
    renderAppointment();
    // Avoid a native submission if the Demo script has not initialized.
    document.getElementById("appointment-save").disabled = false;
})();
