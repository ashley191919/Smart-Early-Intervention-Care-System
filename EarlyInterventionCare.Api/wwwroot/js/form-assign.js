(() => {
    "use strict";

    // Frontend integration fixtures: role-specific tasks in browser storage.
    // Assignment is separate from parent-issued teacher grants; no API or MySQL write.
    // Only SNAP-IV and Clancy currently have integrated questionnaire definitions.
    const form = document.querySelector("#form-assign-form");
    if (!form) return;
    const params = new URLSearchParams(location.search);
    const apiMode = window.MedicalApiConfig?.caseSource === "api" || params.has("caseId") || params.has("source");
    if (apiMode) {
        // Case/Task write contracts and assignment Permission are owned by 11/16.
        // Do not create a second Task store or navigate an API Case into Demo assignment.
        const main = document.querySelector(".form-assign-content");
        const back = document.createElement("a");
        const caseId = params.getAll("caseId").length === 1 && window.MedicalApi?.uuid(params.get("caseId")) ? params.get("caseId") : null;
        back.href = caseId ? "case-detail.html?source=api&caseId=" + encodeURIComponent(caseId) : "cases.html?source=api";
        back.textContent = "← 返回個案資料";
        const panel = document.createElement("section"); panel.className = "panel";
        const heading = document.createElement("h1"); heading.textContent = "指派表單";
        const message = document.createElement("p"); message.textContent = "目前無可指派的問卷。"; message.setAttribute("role", "status");
        panel.append(heading, message); main.replaceChildren(back, panel);
        return;
    }

    const checkboxes = Array.from(form.querySelectorAll('input[name="assignmentItems"]'));
    const rows = Array.from(form.querySelectorAll(".assign-form-row"));
    const formsGroup = form.querySelector("#assign-forms-group");
    const formsError = form.querySelector("#assign-forms-error");
    const selectedCount = form.querySelector("#assign-selected-count");
    const parentCount = form.querySelector("#assign-parent-count");
    const teacherCount = form.querySelector("#assign-teacher-count");
    const deadline = form.querySelector("#assignmentDeadline");
    const deadlineError = form.querySelector("#assign-deadline-error");
    const feedback = form.querySelector("#assign-feedback");
    const invitationDemo = form.querySelector("#assign-invitation-demo");
    const confirmButton = form.querySelector("#assign-confirm");

    function getSelectedItems() {
        return checkboxes.filter((checkbox) => checkbox.checked).map((checkbox) => ({
            formId: checkbox.dataset.formId==='conners'?'clancy':checkbox.dataset.formId,
            respondentRole: checkbox.dataset.respondentRole
        }));
    }

    function updateSelection() {
        const items = getSelectedItems();
        rows.forEach((row) => {
            const selected = Array.from(row.querySelectorAll('input[type="checkbox"]')).some((checkbox) => checkbox.checked);
            row.classList.toggle("is-selected", selected);
        });
        selectedCount.textContent = `已選擇 ${items.length} 個填答項目`;
        parentCount.textContent = `家長：${items.filter((item) => item.respondentRole === "parent").length} 份`;
        teacherCount.textContent = `教師：${items.filter((item) => item.respondentRole === "teacher").length} 份`;
        return items;
    }

    function setFormsError(message) {
        formsError.textContent = message;
        formsError.hidden = message === "";
        formsGroup.setAttribute("aria-invalid", String(message !== ""));
    }

    function validateDeadline() {
        let message = "";
        if (deadline.value.trim() === "") message = "請設定填寫期限";
        else if (!deadline.validity.valid) message = "請設定有效的填寫期限";

        deadlineError.textContent = message;
        deadlineError.hidden = message === "";
        deadline.setAttribute("aria-invalid", String(message !== ""));
        return message === "";
    }

    checkboxes.forEach((checkbox) => {
        // First-load Demo choices are deliberately empty, including browser-restored checkbox state.
        checkbox.checked = false;if(!['snap','conners'].includes(checkbox.dataset.formId)){checkbox.disabled=true;checkbox.closest('article').title='題目與填答流程尚未整合';}
        checkbox.addEventListener("change", () => {
            feedback.hidden = true;
            invitationDemo.hidden = true;
            const items = updateSelection();
            if (formsGroup.getAttribute("aria-invalid") === "true") {
                setFormsError(items.length > 0 ? "" : "請至少選擇一個填答項目");
            }
        });
    });

    ["input", "change"].forEach((eventName) => {
        deadline.addEventListener(eventName, () => {
            feedback.hidden = true;
            invitationDemo.hidden = true;
            if (deadline.getAttribute("aria-invalid") === "true") validateDeadline();
        });
    });

    form.addEventListener("submit", (event) => {
        event.preventDefault();
        feedback.hidden = true;
        invitationDemo.hidden = true;
        // Presentation is fail-closed. This Permission is not registered yet;
        // never substitute case.read/task.read or a role name for assignment authority.
        if (window.StaffAuth?.hasPermission("questionnaire.task.assign") !== true) {
            feedback.textContent = "您沒有指派問卷的權限。";
            feedback.hidden = false;
            return;
        }
        const hasItems = updateSelection().length > 0;
        setFormsError(hasItems ? "" : "請至少選擇一個填答項目");
        const hasDeadline = validateDeadline();

        if (!hasItems) {
            checkboxes[0]?.focus();
            return;
        }
        if (!hasDeadline) {
            deadline.focus();
            return;
        }

        try { const added=SharedDemo.assign(getSelectedItems(),deadline.value); feedback.textContent=`已新增 ${added} 份共用示範任務，可在家長與教師端接續。`; }
        catch(error){feedback.textContent=error.message;feedback.hidden=false;return;}
        feedback.hidden = false;
        // Open the shared task records after fixture assignment.
        invitationDemo.hidden = false;
        window.location.assign("case-detail.html#shared-tasks");
    });

    updateSelection();
    // No approved assignment Permission exists in this branch; do not enable the write.
    confirmButton.disabled = true;
})();
