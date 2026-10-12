const loginCard = document.querySelector(".login-card");
const loginButton = document.querySelector(".login-button");
const loginForm = document.querySelector(".login-form");
const verificationState = document.querySelector(".verification-state");
const loginErrorState = document.querySelector(".login-error-state");
const retryLoginButton = document.querySelector(".retry-login-button");
const staffAccountInput = document.querySelector("#staff-account");
const staffPasswordInput = document.querySelector("#staff-password");
const loginErrorMessage = loginErrorState?.querySelector("p");

let isSubmitting = false;

function showVerificationState() {
    loginCard.classList.remove("is-login-error");
    loginCard.classList.add("is-verifying");
    loginForm.hidden = true;
    verificationState.hidden = false;
    loginErrorState.hidden = true;
}

function showLoginErrorState(message) {
    loginCard.classList.remove("is-verifying");
    loginCard.classList.add("is-login-error");
    loginForm.hidden = true;
    verificationState.hidden = true;
    loginErrorState.hidden = false;
    if (loginErrorMessage) {
        loginErrorMessage.textContent = message;
    }
    retryLoginButton.focus();
}

function showLoginForm() {
    loginCard.classList.remove("is-verifying", "is-login-error");
    loginForm.hidden = false;
    verificationState.hidden = true;
    loginErrorState.hidden = true;
    loginButton.focus();
}

function getErrorMessage(response, body) {
    if (response.status === 401) {
        // The API intentionally returns the same message for unknown, inactive and invalid-password users.
        return "帳號或密碼有誤，請確認後重新輸入。";
    }

    if (response.status === 403) {
        return "登入請求遭安全設定拒絕，請確認您正透過本系統的 HTTPS 網址登入。";
    }

    if (response.status === 503) {
        return "登入服務暫時無法使用，請稍後再試。";
    }

    return typeof body?.message === "string" && body.message.trim()
        ? body.message
        : "登入服務暫時無法使用，請稍後再試。";
}

function isValidStaffSessionResponse(body) {
    return body
        && typeof body.userId === "string"
        && typeof body.username === "string"
        && typeof body.role === "string"
        && typeof body.expiresAt === "string"
        && !Number.isNaN(Date.parse(body.expiresAt));
}

async function submitLogin() {
    if (isSubmitting) return;

    const username = staffAccountInput?.value.trim() ?? "";
    const password = staffPasswordInput?.value ?? "";

    if (!username || !password) {
        showLoginErrorState("請輸入醫療人員帳號與登入密碼。");
        return;
    }

    isSubmitting = true;
    loginButton.disabled = true;
    showVerificationState();

    try {
        const response = await fetch("/api/auth/staff-session", {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            credentials: "same-origin",
            body: JSON.stringify({ username, password })
        });

        let body = null;
        try {
            body = await response.json();
        } catch {
            // A successful login must return the documented JSON response.
        }

        if (!response.ok) {
            if (response.status === 401 && staffPasswordInput) {
                staffPasswordInput.value = "";
            }
            showLoginErrorState(getErrorMessage(response, body));
            return;
        }

        if (!isValidStaffSessionResponse(body)) {
            showLoginErrorState("登入服務回應格式異常，請稍後再試。");
            return;
        }

        // The server stores the JWT in its HttpOnly Secure Cookie. No credential is exposed to JavaScript.
        window.location.assign("/staff-dashboard.html");
    } catch {
        showLoginErrorState("無法連線至登入服務，請確認網路後再試。");
    } finally {
        isSubmitting = false;
        loginButton.disabled = false;
    }
}

loginForm?.addEventListener("submit", (event) => {
    event.preventDefault();
    void submitLogin();
});

retryLoginButton?.addEventListener("click", showLoginForm);
