(() => {
    "use strict";

    // Fictional fixtures from the two case-detail Demo pages, never real patient data.
    // No persistence or cross-page updates. A future API must authorize the case and fields,
    // repeat validation, and audit changes to identifiers and management information.
    const demoCases = {
        "DEMO-001": {
            caseId: "DEMO-001", childName: "王○安", gender: "male", birthDate: "2021-05-12",
            nationalId: "A123456789", address: "新北市測試區示範路1號",
            contactName: "王○美", relationship: "mother", phone: "0912000678",
            email: "wabc@example.com", status: "in-progress", priority: "high",
            detailHref: "case-detail.html"
        },
        "DEMO-024": {
            caseId: "DEMO-024", childName: "林○樂", gender: "female", birthDate: "2021-08-16",
            // Fictional test value: final digit calculated and checked with the existing validator.
            nationalId: "F223456786", address: "新北市測試區示範路2號",
            contactName: "林○婷", relationship: "mother", phone: "0912000321",
            email: "labc@example.com", status: "pending-contact", priority: "normal",
            detailHref: "case-detail-empty.html"
        }
    };
    const form = document.getElementById("case-edit-form");
    if (!form) return;
    const backLink = document.getElementById("edit-back-link");
    const ids = new URLSearchParams(window.location.search).getAll("id");
    const caseId = ids.length === 1 ? ids[0] : "";
    if (!Object.hasOwn(demoCases, caseId)) {
        const loadFeedback = document.getElementById("case-edit-load-feedback");
        loadFeedback.textContent = "未找到此 Demo 個案，請從個案詳細頁進入編輯頁。";
        loadFeedback.classList.add("is-error");
        loadFeedback.hidden = false;
        backLink.textContent = "← 返回個案管理";
        return; // No fallback to another child's data, and saving remains disabled.
    }

    const demo = demoCases[caseId];
    const fields = Array.from(form.querySelectorAll(".create-field-control:not([readonly])"));
    const birthDate = form.querySelector("#birthDate");
    const feedback = form.querySelector("#case-edit-feedback");
    const submitButton = form.querySelector("#edit-case-submit");
    backLink.href = demo.detailHref;
    form.querySelector("#edit-cancel-link").href = demo.detailHref;
    form.querySelector("#caseId").value = demo.caseId;
    fields.forEach((field) => { field.value = demo[field.id]; });

    // Use the same national-ID letter mapping and checksum as case-create.js.
    const nationalIdLetterCodes = {
        A: 10, B: 11, C: 12, D: 13, E: 14, F: 15, G: 16, H: 17, I: 34,
        J: 18, K: 19, L: 20, M: 21, N: 22, O: 35, P: 23, Q: 24, R: 25,
        S: 26, T: 27, U: 28, V: 29, W: 32, X: 30, Y: 31, Z: 33
    };
    function validateTaiwanNationalId(value) {
        if (!/^[A-Z][12][0-9]{8}$/.test(value)) return false;
        const code = nationalIdLetterCodes[value[0]];
        let sum = Math.floor(code / 10) + (code % 10) * 9;
        [8, 7, 6, 5, 4, 3, 2, 1, 1].forEach((weight, index) => {
            sum += Number(value[index + 1]) * weight;
        });
        return sum % 10 === 0;
    }
    function validateTaiwanMobile(value) {
        return /^09[0-9]{8}$/.test(value);
    }
    function localToday() {
        const date = new Date();
        return date.getFullYear() + "-" + String(date.getMonth() + 1).padStart(2, "0") +
            "-" + String(date.getDate()).padStart(2, "0");
    }
    function validCalendarDate(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
        const [year, month, day] = value.split("-").map(Number);
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        return year > 0 && month >= 1 && month <= 12 && day >= 1 && day <= days[month - 1];
    }
    function updateBirthDateLimit() {
        birthDate.max = localToday();
    }
    const allowedValues = {
        gender: ["male", "female", "unspecified"],
        relationship: ["father", "mother", "grandparent", "relative", "other"],
        status: ["pending-contact", "in-progress", "completed"],
        priority: ["normal", "high"]
    };
    function normalizeField(field, limitLength = false) {
        let value = field.value;
        if (field.id === "nationalId") value = value.trim().toUpperCase();
        else if (field.id === "phone") value = value.replace(/[^0-9]/g, "");
        else if (field.type === "email") value = value.trim();
        if (limitLength && ["nationalId", "phone"].includes(field.id)) value = value.slice(0, 10);
        if (field.value !== value) field.value = value;
    }
    function validateField(field) {
        const value = field.value.trim();
        let message = "";
        if (field.required && !value) message = "此欄位為必填";
        else if (allowedValues[field.id] && !allowedValues[field.id].includes(value)) message = "請選擇有效的選項";
        else if (field.id === "birthDate") {
            updateBirthDateLimit();
            if (!validCalendarDate(value)) message = "請輸入有效的出生日期";
            else if (value > localToday()) message = "出生日期不可選擇未來日期";
        } else if (field.id === "nationalId" && !validateTaiwanNationalId(value)) message = "請輸入有效的身分證字號";
        else if (field.id === "phone" && !validateTaiwanMobile(value)) message = "請輸入有效的手機號碼";
        else if (field.type === "email" && value && field.validity.typeMismatch) message = "請輸入有效的電子郵件地址";
        const error = form.querySelector("#" + field.id + "-error");
        error.textContent = message;
        error.hidden = !message;
        field.setAttribute("aria-invalid", String(Boolean(message)));
        return !message;
    }
    function handleFieldEdit(event) {
        feedback.hidden = true;
        if (event.isComposing) return;
        const field = event.currentTarget;
        normalizeField(field, true);
        if (field.getAttribute("aria-invalid") === "true") validateField(field);
    }
    fields.forEach((field) => {
        field.addEventListener("input", handleFieldEdit);
        field.addEventListener("change", handleFieldEdit);
    });
    fields.filter((field) => ["nationalId", "phone"].includes(field.id)).forEach((field) => {
        field.addEventListener("paste", (event) => {
            if (!event.clipboardData) return;
            event.preventDefault();
            const start = field.selectionStart ?? field.value.length;
            const end = field.selectionEnd ?? start;
            field.value = field.value.slice(0, start) + event.clipboardData.getData("text") + field.value.slice(end);
            handleFieldEdit({ currentTarget: field });
        });
    });
    birthDate.addEventListener("focus", updateBirthDateLimit);
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        fields.forEach((field) => normalizeField(field));
        const invalidFields = fields.filter((field) => !validateField(field));
        feedback.classList.toggle("is-error", invalidFields.length > 0);
        feedback.hidden = false;
        if (invalidFields.length) {
            feedback.textContent = "請確認 " + invalidFields.length + " 個欄位的內容後再試一次。";
            invalidFields[0].focus();
            return;
        }
        feedback.textContent = "Demo 模式，尚未真正儲存。變更未送出，也不會更新個案詳細頁或列表。";
    });
    updateBirthDateLimit();
    form.hidden = false;
    submitButton.disabled = false;
})();
