// Adapted selectively from the isolated v5.3 consumer. Authentication remains owned by StaffAuth.
(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.MedicalApi = api;
})(typeof window === "undefined" ? globalThis : window, function () {
    "use strict";
    const messages = {
        400: "請確認輸入資料。", 401: "登入狀態已失效，請重新登入。",
        403: "您無法存取此項資料或操作。", 404: "找不到可存取的資料。",
        409: "資料狀態已更新，請重新載入。", 429: "操作過於頻繁，請稍後再試。",
        503: "服務暫時無法使用，請稍後重試。"
    };
    class MedicalApiError extends Error {
        constructor(status, code) {
            super(messages[status] || (code === "API_ORIGIN_NOT_APPROVED" ? "API 連線設定尚未通過安全確認。" :
                code === "REQUEST_TIMEOUT" ? "連線逾時，請稍後重試。" :
                code === "INVALID_RESPONSE" ? "服務回應格式異常，請稍後重試。" : "目前無法連線至服務。"));
            this.name = "MedicalApiError"; this.status = status; this.code = code;
        }
    }
    const invalid = () => { throw new MedicalApiError(0, "INVALID_RESPONSE"); };
    const uuid = value => typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
    const text = value => typeof value === "string" && value.trim().length > 0;
    const time = value => typeof value === "string" && /Z$/.test(value) && Number.isFinite(Date.parse(value));
    const caseStatuses = new Set(["NEW", "TO_CONTACT", "APPOINTED", "WAITING_FORM", "FORM_COMPLETED", "COMPLETED"]);
    const taskStatuses = new Set(["PENDING", "IN_PROGRESS", "SUBMITTED", "CANCELLED"]);
    const birthDate = value => typeof value === "string" && /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value) &&
        value.slice(0, 4) !== "0000" && Number.isFinite(Date.parse(value + "T00:00:00Z")) &&
        new Date(value + "T00:00:00Z").toISOString().slice(0, 10) === value;
    const caseTime = value => typeof value === "string" &&
        /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) &&
        birthDate(value.slice(0, 10)) && Number.isFinite(Date.parse(value));
    function maskName(value) {
        const chars = Array.from(value.trim());
        return chars.length < 2 ? "○" : chars.length === 2 ? chars[0] + "○" :
            chars[0] + "○".repeat(chars.length - 2) + chars[chars.length - 1];
    }
    function maskNationalId(value) {
        if (!value.trim() || value === "未提供") return "未提供";
        return /^[A-Z]\d{9}$/.test(value) || /^[A-Z]\d{2}\*{7}$/.test(value) ? value.slice(0, 3) + "*******" : "已遮罩";
    }
    function maskAddress(value) {
        if (!value.trim() || value === "未提供") return "未提供";
        const city = value.match(/^([^\s]{2}[市縣])/);
        return city ? city[1] + "○○區○○路" : "已遮罩";
    }
    function caseDto(value) {
        if (!value || !uuid(value.caseId) || !text(value.caseCode) || !text(value.childName) ||
            !birthDate(value.birthDate) || !["MALE", "FEMALE", "UNKNOWN"].includes(value.sex) ||
            typeof value.nationalId !== "string" || typeof value.address !== "string" ||
            !caseStatuses.has(value.caseStatus) || !Number.isInteger(value.revision) || value.revision < 0 || value.revision > 4294967295 ||
            !caseTime(value.createdAtUtc) || !caseTime(value.updatedAtUtc)) invalid();
        // Validate all eleven producer fields, but never return unmasked child identifiers to a page.
        return { caseId: value.caseId, caseCode: value.caseCode, childName: maskName(value.childName),
            birthDate: value.birthDate, sex: value.sex, nationalId: maskNationalId(value.nationalId), address: maskAddress(value.address),
            caseStatus: value.caseStatus, revision: value.revision, createdAtUtc: value.createdAtUtc, updatedAtUtc: value.updatedAtUtc };
    }
    function taskDto(value, caseId) {
        if (!value || !uuid(value.taskId) || value.caseId !== caseId || !uuid(value.questionnaireId) ||
            !uuid(value.questionnaireVersionId) || !text(value.questionnaireTitle) || !text(value.versionNumber) ||
            !["PARENT", "TEACHER"].includes(value.respondentRole) || !Number.isSafeInteger(value.assignmentRound) ||
            value.assignmentRound < 1 || typeof value.isRequired !== "boolean" || !taskStatuses.has(value.taskStatus) ||
            !time(value.createdAtUtc) || !time(value.updatedAtUtc) ||
            (value.submittedAtUtc !== null && !time(value.submittedAtUtc)) ||
            (value.taskStatus === "SUBMITTED" && !time(value.submittedAtUtc)) ||
            (value.taskStatus !== "SUBMITTED" && value.submittedAtUtc !== null)) invalid();
        const { taskId, questionnaireId, questionnaireVersionId, questionnaireTitle, versionNumber,
            respondentRole, assignmentRound, isRequired, taskStatus, submittedAtUtc } = value;
        return { taskId, caseId, questionnaireId, questionnaireVersionId, questionnaireTitle, versionNumber,
            respondentRole, assignmentRound, isRequired, taskStatus, submittedAtUtc };
    }
    async function readJson(response) {
        let data = null;
        try { data = await response.json(); } catch { /* Empty/HTML failures must retain their HTTP status. */ }
        if (!response.ok) {
            const code = typeof data?.code === "string" && /^[A-Z][A-Z0-9_]{0,63}$/.test(data.code) ? data.code : "HTTP_ERROR";
            throw new MedicalApiError(response.status, code);
        }
        if (response.status !== 204 && data === null) invalid();
        return data;
    }
    function createTransport({ apiBaseUrl = "", timeoutMs = 15000,
        origin = globalThis.location?.origin || globalThis.window?.location?.origin,
        fetchImpl = (...args) => globalThis.fetch(...args) } = {}) {
        let base;
        try {
            const site = new URL(origin);
            base = new URL(apiBaseUrl || "/", site.origin);
            if (!["https:", "http:"].includes(site.protocol) || base.origin !== site.origin ||
                base.username || base.password || base.search || base.hash || /[%\\]/.test(base.pathname) || base.pathname.includes("//")) throw new Error();
        } catch { throw new MedicalApiError(0, "API_ORIGIN_NOT_APPROVED"); }
        if (!Number.isFinite(timeoutMs) || timeoutMs < 1 || timeoutMs > 120000)
            throw new MedicalApiError(0, "INVALID_CONFIGURATION");
        const prefix = base.pathname.replace(/\/$/, "");
        function resolve(path) {
            // Fixed API paths only; no redirects, encoded traversal, Demo, or credential-bearing URLs.
            if (typeof path !== "string" || !path.startsWith("/api/") || /[#%\\]/.test(path) ||
                /^\/api\/medical-demo(?:\/|\?|$)/.test(path) || path.split("?")[0].split("/").some(p => p === "." || p === ".."))
                throw new MedicalApiError(0, "API_ORIGIN_NOT_APPROVED");
            return prefix + path;
        }
        async function fetchApi(path, options = {}) {
            const url = resolve(path); // Reject before sending credentials or looking up CSRF.
            const controller = new AbortController();
            const abort = () => controller.abort();
            options.signal?.addEventListener("abort", abort, { once: true });
            if (options.signal?.aborted) abort();
            let timedOut = false;
            const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
            try {
                const headers = new Headers(options.headers || {});
                headers.set("Accept", "application/json");
                return await fetchImpl(url, { ...options, headers, credentials: "same-origin",
                    cache: "no-store", redirect: "error", signal: controller.signal });
            } catch (error) {
                if (options.signal?.aborted) throw new DOMException("Aborted", "AbortError");
                throw new MedicalApiError(0, timedOut ? "REQUEST_TIMEOUT" : "NETWORK_ERROR");
            } finally {
                clearTimeout(timer); options.signal?.removeEventListener("abort", abort);
            }
        }
        return Object.freeze({ resolve, fetch: fetchApi });
    }
    function createClient({ cookieFetch = (...args) => {
        if (!globalThis.window?.StaffAuth) throw new MedicalApiError(0, "STAFF_AUTH_REQUIRED");
        return globalThis.window.StaffAuth.cookieFetch(...args);
    } } = {}) {
        // No login/logout/session state here. Production requests go through the existing Session owner.
        async function get(path, signal) {
            try { return await readJson(await cookieFetch(path, { method: "GET", signal })); }
            catch (error) {
                if (error instanceof MedicalApiError || error?.name === "AbortError") throw error;
                throw new MedicalApiError(0, "NETWORK_ERROR");
            }
        }
        const id = value => { if (!uuid(value)) throw new MedicalApiError(400, "INVALID_INPUT"); return value; };
        function unique(items, key) {
            if (new Set(items.map(item => item[key])).size !== items.length) invalid();
            return items;
        }
        return Object.freeze({
            async cases(page = 1, pageSize = 20, signal) {
                if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100)
                    throw new MedicalApiError(400, "INVALID_PAGE");
                const values = await get(`/api/cases?page=${page}&pageSize=${pageSize}`, signal);
                if (!Array.isArray(values) || values.length > pageSize) invalid();
                return unique(values.map(caseDto), "caseId");
            },
            async caseDetail(caseId, signal) {
                const result = caseDto(await get(`/api/cases/${id(caseId)}`, signal));
                if (result.caseId !== caseId) invalid(); return result;
            },
            async tasks(caseId, signal) {
                const values = await get(`/api/cases/${id(caseId)}/tasks`, signal);
                if (!Array.isArray(values)) invalid();
                return unique(values.map(value => taskDto(value, caseId)), "taskId");
            }
        });
    }
    return { createTransport, createClient, readJson, MedicalApiError, uuid, caseDto, taskDto };
});
