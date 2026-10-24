const loginCard = document.querySelector(".login-card");
const loginButton = document.querySelector(".login-button");
const loginForm = document.querySelector(".login-form");
const verificationState = document.querySelector(".verification-state");
const loginErrorState = document.querySelector(".login-error-state");
const retryLoginButton = document.querySelector(".retry-login-button");
const organizationCodeInput = document.querySelector("#organization-code");
const staffAccountInput = document.querySelector("#staff-account");
const staffPasswordInput = document.querySelector("#staff-password");
const loginErrorMessage = loginErrorState?.querySelector("p");

let isSubmitting = false;
const medicalTransport = window.MedicalApi.createTransport(window.MedicalApiConfig);

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
        // Organization, account, role and password failures intentionally share one message.
        return "登入資料有誤，請確認後重新輸入。";
    }

    if (response.status === 403) {
        return "登入請求遭安全設定拒絕，請確認您正透過本系統的 HTTPS 網址登入。";
    }

    if (response.status === 503) {
        return "登入服務暫時無法使用，請稍後再試。";
    }

    // Never display arbitrary backend HTML, SQL or credential-bearing error messages.
    return new window.MedicalApi.MedicalApiError(response.status, "HTTP_ERROR").message;
}

function isValidStaffSessionResponse(body) {
    return body
        && typeof body.userId === "string"
        && typeof body.username === "string"
        && typeof body.role === "string"
        && typeof body.expiresAt === "string"
        && !Number.isNaN(Date.parse(body.expiresAt));
}

async function addCsrfForExistingSession(headers) {
    // A valid existing HttpOnly session cookie makes this POST subject to the same
    // cookie-write CSRF protection as every other unsafe request. We deliberately
    // do not inspect browser cookies or retain the token after this request.
    const response = await medicalTransport.fetch("/api/auth/csrf", {
        method: "GET",
        headers: { "Accept": "application/json" },
        credentials: "same-origin"
    });

    if (response.status === 401) {
        // No active session exists, so the login POST is an anonymous request.
        return;
    }

    let body = null;
    try {
        body = await response.json();
    } catch {
        // The validated response format is checked below.
    }

    if (!response.ok || typeof body?.requestToken !== "string" || !body.requestToken) {
        throw new Error("無法取得安全驗證資訊，請稍後再試。");
    }

    headers.set("X-EarlyCare-CSRF", body.requestToken);
}

async function submitLogin() {
    if (isSubmitting) return;

    const organizationCode = organizationCodeInput?.value.trim() ?? "";
    const username = staffAccountInput?.value.trim() ?? "";
    const password = staffPasswordInput?.value ?? "";

    if (!organizationCode || !username || !password) {
        showLoginErrorState("請輸入單位代碼、醫療人員帳號與登入密碼。");
        return;
    }
    if (organizationCode.length > 50 || username.length > 100) {
        showLoginErrorState("單位代碼不得超過 50 字元，帳號不得超過 100 字元。");
        return;
    }

    isSubmitting = true;
    loginButton.disabled = true;
    showVerificationState();

    try {
        const headers = new Headers({
            "Accept": "application/json",
            "Content-Type": "application/json"
        });
        await addCsrfForExistingSession(headers);
        const response = await medicalTransport.fetch("/api/auth/staff-session", {
            method: "POST",
            headers,
            credentials: "same-origin",
            body: JSON.stringify({ organizationCode, username, password })
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
