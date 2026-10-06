const loginCard = document.querySelector(".login-card");
const loginButton = document.querySelector(".login-button");
const loginForm = document.querySelector(".login-form");
const verificationState = document.querySelector(".verification-state");
const loginErrorState = document.querySelector(".login-error-state");
const retryLoginButton = document.querySelector(".retry-login-button");

function showVerificationState() {
    loginCard.classList.remove("is-login-error");
    loginCard.classList.add("is-verifying");
    loginForm.hidden = true;
    verificationState.hidden = false;
    loginErrorState.hidden = true;
}

function showLoginErrorState() {
    loginCard.classList.remove("is-verifying");
    loginCard.classList.add("is-login-error");
    loginForm.hidden = true;
    verificationState.hidden = true;
    loginErrorState.hidden = false;
    retryLoginButton.focus();
}

function showLoginForm() {
    loginCard.classList.remove("is-verifying", "is-login-error");
    loginForm.hidden = false;
    verificationState.hidden = true;
    loginErrorState.hidden = true;
    loginButton.focus();
}

loginButton?.addEventListener("click", () => {
    showVerificationState();

    // TODO: 串接 ASP.NET Core Login API 後移除此模擬失敗流程。
    window.setTimeout(showLoginErrorState, 1200);
});

retryLoginButton?.addEventListener("click", showLoginForm);
