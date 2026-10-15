(() => {
    "use strict";

    const medicalRoles = new Set(["CASE_MANAGER", "MEDICAL_STAFF"]);
    const protectedAreas = Array.from(document.querySelectorAll("[data-staff-protected]"));
    const errorArea = document.querySelector("[data-staff-auth-error]");
    let currentUser = null;

    class StaffAuthError extends Error {}

    function showError(message) {
        currentUser = null;
        protectedAreas.forEach((area) => { area.hidden = true; });
        if (errorArea) {
            errorArea.textContent = message;
            errorArea.hidden = false;
        }
    }

    function showProtectedAreas() {
        if (errorArea) errorArea.hidden = true;
        protectedAreas.forEach((area) => { area.hidden = false; });
    }

    function clearProtectedContent() {
        protectedAreas.forEach((area) => {
            area.hidden = true;
            area.replaceChildren();
        });
    }

    function redirectToLogin() {
        window.location.replace("/staff-login.html");
    }

    async function parseJson(response) {
        try {
            return await response.json();
        } catch {
            return null;
        }
    }

    function ensureSameOrigin(path) {
        const url = new URL(path, window.location.origin);
        if (url.origin !== window.location.origin) {
            throw new StaffAuthError("Cookie API 請求只能送往本系統。" );
        }
        return url.pathname + url.search;
    }

    async function getCsrfToken() {
        const response = await fetch("/api/auth/csrf", {
            method: "GET",
            headers: { "Accept": "application/json" },
            credentials: "same-origin"
        });
        const body = await parseJson(response);
        if (response.status === 401) {
            clearProtectedContent();
            redirectToLogin();
            throw new StaffAuthError("登入已逾時，請重新登入。");
        }
        if (!response.ok || typeof body?.requestToken !== "string" || !body.requestToken) {
            throw new StaffAuthError("無法取得安全驗證資訊，請稍後再試。");
        }
        return body.requestToken;
    }

    function isUnsafeMethod(method) {
        return !["GET", "HEAD", "OPTIONS"].includes((method || "GET").toUpperCase());
    }

    async function cookieFetch(path, options = {}) {
        const method = (options.method || "GET").toUpperCase();
        const headers = new Headers(options.headers || {});
        if (isUnsafeMethod(method)) {
            headers.set("X-EarlyCare-CSRF", await getCsrfToken());
        }
        return fetch(ensureSameOrigin(path), {
            ...options,
            method,
            headers,
            credentials: "same-origin"
        });
    }

    async function requireStaffSession() {
        let response;
        try {
            response = await fetch("/api/auth/me", {
                method: "GET",
                headers: { "Accept": "application/json" },
                credentials: "same-origin"
            });
        } catch {
            showError("目前無法連線至登入服務，請確認網路後再試。");
            return null;
        }

        if (response.status === 401) {
            clearProtectedContent();
            redirectToLogin();
            return null;
        }

        if (response.status === 403) {
            showError("您沒有使用醫療端工作區的權限。");
            return null;
        }

        if (!response.ok) {
            showError("登入服務暫時無法使用，請稍後再試。");
            return null;
        }

        const user = await parseJson(response);
        if (!user || typeof user.userId !== "string" || typeof user.username !== "string" ||
            typeof user.role !== "string") {
            showError("登入服務回應格式異常，請稍後再試。");
            return null;
        }

        if (!medicalRoles.has(user.role)) {
            showError("您沒有使用醫療端工作區的權限。");
            return null;
        }

        currentUser = Object.freeze({
            userId: user.userId,
            username: user.username,
            role: user.role,
            permissions: Array.isArray(user.permissions) ? [...user.permissions] : []
        });
        showProtectedAreas();
        document.dispatchEvent(new CustomEvent("staff-authenticated", { detail: currentUser }));
        return currentUser;
    }

    async function logout() {
        let response;
        try {
            response = await cookieFetch("/api/auth/logout", { method: "POST" });
        } catch (error) {
            if (error instanceof StaffAuthError) throw error;
            throw new StaffAuthError("無法連線至登出服務，請稍後再試。");
        }

        if (response.status === 204) {
            clearProtectedContent();
            redirectToLogin();
            return;
        }

        if (response.status === 401) {
            clearProtectedContent();
            showError("登入狀態已失效，無法確認登出結果，請重新登入。");
            throw new StaffAuthError("登入狀態已失效，無法確認登出結果，請重新登入。");
        }

        if (response.status === 403) {
            throw new StaffAuthError("登出請求未通過安全驗證，尚未完成登出。");
        }

        throw new StaffAuthError("登出服務暫時無法使用，請稍後再試。");
    }

    window.StaffAuth = Object.freeze({
        requireStaffSession,
        getCurrentUser: () => currentUser,
        cookieFetch,
        logout
    });

    void requireStaffSession();
})();
