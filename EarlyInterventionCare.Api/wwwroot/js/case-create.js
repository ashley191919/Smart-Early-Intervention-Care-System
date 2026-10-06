(() => {
    "use strict";

    // Future backend defaults for a new case: status = pending-contact, priority = normal.
    // This Demo validates fields only; it does not create or persist a case.
    // The future Web API must repeat these validations; client-side checks are not a security boundary.
    // National ID letter codes and checksum weights:
    // https://www.cksh.tp.edu.tw/wp-content/uploads/doc/teaching/word%20%E6%BC%94%E7%AE%97%E6%B3%95%E8%AA%AA%E6%98%8E.pdf
    const nationalIdLetterCodes = {
        A: 10, B: 11, C: 12, D: 13, E: 14, F: 15, G: 16, H: 17, I: 34,
        J: 18, K: 19, L: 20, M: 21, N: 22, O: 35, P: 23, Q: 24, R: 25,
        S: 26, T: 27, U: 28, V: 29, W: 32, X: 30, Y: 31, Z: 33
    };

    function validateTaiwanNationalId(value) {
        // Only national IDs: the second character is 1 or 2, not a residence permit code.
        if (value.length !== 10 || !/^[A-Z][12][0-9]{8}$/.test(value)) return false;

        const letterCode = nationalIdLetterCodes[value[0]];
        let sum = Math.floor(letterCode / 10) + (letterCode % 10) * 9;
        const weights = [8, 7, 6, 5, 4, 3, 2, 1, 1];
        for (let index = 0; index < weights.length; index += 1) {
            sum += Number(value[index + 1]) * weights[index];
        }
        return sum % 10 === 0;
    }

    function validateTaiwanMobile(value) {
        return value.length === 10 && /^09[0-9]{8}$/.test(value);
    }

    function normalizeContactField(field, limitLength = false) {
        let value = field.value;
        if (field.id === "nationalId") value = value.trim().toUpperCase();
        else if (field.id === "phone") value = value.replace(/[^0-9]/g, "");
        else return;

        if (limitLength) value = value.slice(0, 10);
        if (field.value !== value) field.value = value;
    }

    const form = document.querySelector("#case-create-form");
    if (!form) return;

    const fields = Array.from(form.querySelectorAll(".create-field-control"));
    const contactFormatFields = fields.filter((field) => field.id === "nationalId" || field.id === "phone");
    const birthDate = form.querySelector("#birthDate");
    const feedback = form.querySelector("#case-create-feedback");
    const submitButton = form.querySelector("#create-case-submit");

    function updateBirthDateLimit() {
        // Use the user's local calendar date, not a UTC date that may differ near midnight.
        const today = new Date();
        const month = String(today.getMonth() + 1).padStart(2, "0");
        const day = String(today.getDate()).padStart(2, "0");
        birthDate.max = `${today.getFullYear()}-${month}-${day}`;
    }

    updateBirthDateLimit();
    birthDate.addEventListener("focus", updateBirthDateLimit);

    function validateField(field, checkContactFormat = true) {
        // Refresh on validation as well, in case this page stays open overnight.
        if (field === birthDate) updateBirthDateLimit();
        let message = "";
        if (field.required && field.value.trim() === "") {
            message = "此欄位為必填";
        } else if (field === birthDate && field.validity.rangeOverflow) {
            message = "出生日期不可選擇未來日期";
        } else if (field.type === "email" && field.value.trim() !== "" && field.validity.typeMismatch) {
            message = "請輸入有效的電子郵件地址";
        } else if (checkContactFormat && field.id === "nationalId" && !validateTaiwanNationalId(field.value)) {
            message = "請輸入有效的身分證字號";
        } else if (checkContactFormat && field.id === "phone" && !validateTaiwanMobile(field.value)) {
            message = "請輸入有效的手機號碼";
        }

        const error = form.querySelector(`#${field.id}-error`);
        error.textContent = message;
        error.hidden = message === "";
        field.setAttribute("aria-invalid", String(message !== ""));
        return message === "";
    }

    function handleFieldEdit(event) {
        feedback.hidden = true;
        const field = event.currentTarget;
        if (contactFormatFields.includes(field) && event.isComposing) return;
        normalizeContactField(field, true);
        if (field.getAttribute("aria-invalid") === "true") validateField(field);
    }

    fields.forEach((field) => {
        field.addEventListener("input", handleFieldEdit);
        field.addEventListener("change", handleFieldEdit);
    });

    // Normalize pasted text before maxlength can truncate spaces or phone separators.
    contactFormatFields.forEach((field) => {
        field.addEventListener("paste", (event) => {
            if (!event.clipboardData) return;
            event.preventDefault();
            const start = field.selectionStart ?? field.value.length;
            const end = field.selectionEnd ?? start;
            field.value = field.value.slice(0, start) + event.clipboardData.getData("text") + field.value.slice(end);
            handleFieldEdit({ currentTarget: field });
        });
    });

    form.addEventListener("submit", (event) => {
        event.preventDefault();
        // Normalize again for autofill, then run existing required/email checks before contact formats.
        // Do not truncate at submit: an overlong programmatic value must fail validation.
        contactFormatFields.forEach((field) => normalizeContactField(field));
        const invalidFields = fields.filter((field) => !validateField(field, false));
        contactFormatFields.forEach((field) => {
            if (!invalidFields.includes(field) && !validateField(field)) invalidFields.push(field);
        });
        feedback.classList.toggle("is-error", invalidFields.length > 0);
        feedback.hidden = false;

        if (invalidFields.length > 0) {
            feedback.textContent = `請確認 ${invalidFields.length} 個欄位的內容後再試一次。`;
            invalidFields[0].focus();
            return;
        }

        feedback.textContent = "目前為前端 Demo，個案資料尚未送出。";
    });

    // Enable submission only after the handler is ready, avoiding an accidental native form submission.
    submitButton.disabled = false;
})();
