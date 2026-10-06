const resetPasswordForm = document.querySelector(".reset-password-form");
const newPasswordInput = document.querySelector("#new-password");
const confirmPasswordInput = document.querySelector("#confirm-password");
const resetPasswordButton = document.querySelector(".reset-password-form .login-button");
const passwordError = document.querySelector("#password-error");

function clearPasswordError() {
    passwordError.hidden = true;
    passwordError.textContent = "";

    [newPasswordInput, confirmPasswordInput].forEach((input) => {
        input.closest(".form-field").classList.remove("is-invalid");
        input.removeAttribute("aria-invalid");
    });
}

function showPasswordError(message, inputs) {
    passwordError.textContent = message;
    passwordError.hidden = false;

    inputs.forEach((input) => {
        input.closest(".form-field").classList.add("is-invalid");
        input.setAttribute("aria-invalid", "true");
    });
}

function validatePasswordInputs() {
    const newPassword = newPasswordInput.value;
    const confirmPassword = confirmPasswordInput.value;

    clearPasswordError();

    if (!newPassword && !confirmPassword) {
        showPasswordError("請輸入新密碼與確認新密碼", [newPasswordInput, confirmPasswordInput]);
        newPasswordInput.focus();
        return;
    }

    if (!newPassword) {
        showPasswordError("請輸入新密碼", [newPasswordInput]);
        newPasswordInput.focus();
        return;
    }

    if (!confirmPassword) {
        showPasswordError("請再次輸入新密碼", [confirmPasswordInput]);
        confirmPasswordInput.focus();
        return;
    }

    if (newPassword !== confirmPassword) {
        showPasswordError("兩次輸入的密碼不一致", [newPasswordInput, confirmPasswordInput]);
        confirmPasswordInput.focus();
        return;
    }

    // TODO: 串接 ASP.NET Core Reset Password API
    console.log("Password validation passed.");
}

resetPasswordButton?.addEventListener("click", validatePasswordInputs);

[newPasswordInput, confirmPasswordInput].forEach((input) => {
    input?.addEventListener("input", clearPasswordError);
});

resetPasswordForm?.addEventListener("submit", (event) => event.preventDefault());
