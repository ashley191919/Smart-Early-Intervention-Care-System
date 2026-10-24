(() => {
    "use strict";

    const medicalRoles = new Set(["CASE_MANAGER", "MEDICAL_STAFF"]);
    const protectedAreas = Array.from(document.querySelectorAll("[data-staff-protected]"));
    const errorArea = document.querySelector("[data-staff-auth-error]");
    // Only the four codes registered by AddFirstBatchPermissionPolicies are presentation contracts.
    const registeredPermissions = new Set([
        "case.read", "case.status.update", "case.status_history.read", "questionnaire.task.read"
    ]);
    let currentUser = null;
    let permissionState = "pending";
    let sessionRevision = 0;

    class StaffAuthError extends Error {}

    const transport = window.MedicalApi.createTransport(window.MedicalApiConfig);

    function invalidatePermissions(state) {
        sessionRevision++;
        currentUser = null;
        permissionState = state;
        document.dispatchEvent(new CustomEvent("staff-permissions-changed"));
    }

    function hasPermission(permission) {
        return permissionState === "ready" && registeredPermissions.has(permission) &&
            currentUser?.permissions.includes(permission) === true;
    }

    function showError(message) {
        invalidatePermissions("error");
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
        invalidatePermissions("expired");
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
        transport.resolve(path);
        return path;
    }

    async function getCsrfToken() {
        let response;
        try {
            response = await transport.fetch("/api/auth/csrf", {
                method: "GET",
                headers: { "Accept": "application/json" },
                credentials: "same-origin"
            });
        } catch {
            invalidatePermissions("error");
            throw new StaffAuthError("無法取得安全驗證資訊，請稍後再試。");
        }
        const body = await parseJson(response);
        if (response.status === 401) {
            clearProtectedContent();
            redirectToLogin();
            throw new StaffAuthError("登入已逾時，請重新登入。");
        }
        if (!response.ok || typeof body?.requestToken !== "string" || !body.requestToken) {
            invalidatePermissions("error");
            throw new StaffAuthError("無法取得安全驗證資訊，請稍後再試。");
        }
        return body.requestToken;
    }

    function isUnsafeMethod(method) {
        return !["GET", "HEAD", "OPTIONS"].includes((method || "GET").toUpperCase());
    }

    async function cookieFetch(path, options = {}) {
        ensureSameOrigin(path);
        const method = (options.method || "GET").toUpperCase();
        const headers = new Headers(options.headers || {});
        if (isUnsafeMethod(method)) {
            headers.set("X-EarlyCare-CSRF", await getCsrfToken());
        }
        let response;
        try {
            response = await transport.fetch(path, {
                ...options,
                method,
                headers,
                credentials: "same-origin"
            });
        } catch (error) {
            invalidatePermissions("error");
            throw error;
        }
        if (response.status === 401) {
            clearProtectedContent();
            redirectToLogin();
        } else if (!response.ok) {
            // Keep the response for its caller, but stop using stale permissions.
            invalidatePermissions(response.status === 403 ? "forbidden" : "error");
        }
        return response;
    }

    async function requireStaffSession() {
        invalidatePermissions("pending");
        const revision = sessionRevision;
        let response;
        try {
            response = await transport.fetch("/api/auth/me", {
                method: "GET",
                headers: { "Accept": "application/json" },
                credentials: "same-origin"
            });
        } catch {
            if (revision !== sessionRevision) return null;
            showError("目前無法連線至登入服務，請確認網路後再試。");
            return null;
        }

        if (revision !== sessionRevision) return null;

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
        if (revision !== sessionRevision) return null;
        if (!user || typeof user.userId !== "string" || typeof user.username !== "string" ||
            typeof user.role !== "string") {
            showError("登入服務回應格式異常，請稍後再試。");
            return null;
        }

        if (!medicalRoles.has(user.role)) {
            showError("您沒有使用醫療端工作區的權限。");
            return null;
        }

        // A missing/mixed/malformed list rejects all operations, including otherwise valid entries.
        const validPermissions = Array.isArray(user.permissions) && user.permissions.every((code) =>
            typeof code === "string" && /^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$/.test(code));
        currentUser = Object.freeze({
            userId: user.userId,
            username: user.username,
            role: user.role,
            permissions: Object.freeze(validPermissions ? [...new Set(user.permissions)] : [])
        });
        permissionState = validPermissions ? "ready" : "error";
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
        hasPermission,
        getPermissionState: () => permissionState,
        cookieFetch,
        logout
    });

    // A restored page must revalidate rather than reuse permissions from its old session.
    window.addEventListener("pagehide", () => invalidatePermissions("expired"));
    window.addEventListener("pageshow", (event) => {
        if (event.persisted) void requireStaffSession();
    });
    void requireStaffSession();
})();
