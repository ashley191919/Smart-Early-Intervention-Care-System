(() => {
    "use strict";

    // 專題展示用設定：不送出、不儲存，也不建立實際指派紀錄。
    // 每個 { formId, respondentRole } 是獨立填答任務；同一表單的 parent / teacher
    // 未來必須有不同的任務與填答結果，不能共用答案。
    // 醫療人員決定收集哪些表單及由哪些角色填寫；勾選 teacher 不等於教師已獲授權。
    // 家長負責教師填答授權；教師取得家長提供的授權碼並通過驗證後，
    // 才能看到醫療人員當初指定的教師填答任務。後端必須分開管理指派與授權狀態。
    // 未來教師授權需由後端建立不可猜測的隨機 token / authorization code，
    // 不直接使用 caseId / parentId / childId；需包含任務對應、期限、授權及失效狀態。
    // 本頁不產生任何授權資訊；驗證通過後只導向固定的填答邀請 Demo 頁。
    const form = document.querySelector("#form-assign-form");
    if (!form) return;

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
            formId: checkbox.dataset.formId,
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
        checkbox.checked = false;
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

        feedback.textContent = "Demo：表單指派設定完成，目前尚未建立實際指派紀錄。";
        feedback.hidden = false;
        // Direct Demo navigation only: no assignment is saved and no selection or deadline is transferred.
        invitationDemo.hidden = false;
        window.location.assign("invitation-success.html");
    });

    updateSelection();
    // Enable confirmation only after the handler prevents native form submission.
    confirmButton.disabled = false;
})();
