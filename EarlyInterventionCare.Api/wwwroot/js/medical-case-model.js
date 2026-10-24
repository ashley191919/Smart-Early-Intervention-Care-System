(function (root, factory) {
    const api = typeof module === "object" && module.exports ? require("./medical-api.js") : root.MedicalApi;
    const model = factory(api);
    if (typeof module === "object" && module.exports) module.exports = model;
    else root.MedicalCases = model;
})(typeof window === "undefined" ? globalThis : window, function (api) {
    "use strict";
    const labels = { NEW: "新建", TO_CONTACT: "待聯繫", APPOINTED: "已預約", WAITING_FORM: "待填表",
        FORM_COMPLETED: "表單已完成", COMPLETED: "已結案" };
    const missing = "未提供";
    function route(search = "", config = {}) {
        const params = new URLSearchParams(search);
        const sources = params.getAll("source"), ids = params.getAll("caseId");
        // An explicit ID must never select a static Demo child's page, including legacy/wrong ID parameters.
        const requested = config.caseSource || "demo";
        const invalid = sources.length > 1 || ids.length > 1 || params.has("id") ||
            sources.some(s => s !== "api") || !["api", "demo"].includes(requested);
        return Object.freeze({ mode: requested === "api" || sources.length || ids.length || invalid ? "api" : "demo",
            caseId: ids.length === 1 ? ids[0] : null, invalid });
    }
    function toViewModel(dto) {
        const value = api.caseDto(dto);
        return Object.freeze({ id: value.caseId, name: value.childName, status: value.caseStatus,
            statusLabel: labels[value.caseStatus], statusClass: value.caseStatus === "COMPLETED" ? "completed" :
                ["NEW", "TO_CONTACT"].includes(value.caseStatus) ? "pending-contact" : "in-progress",
            code: value.caseCode, updatedAt: value.updatedAtUtc, priority: null, contactName: missing,
            sex: missing, birthDate: missing, nationalId: missing, address: missing, relationship: missing,
            phone: missing, email: missing,
            detailHref: "case-detail.html?source=api&caseId=" + encodeURIComponent(value.caseId) });
    }
    function createApiAdapter(client) {
        return Object.freeze({ mode: "api", list: (page, pageSize) => client.cases(page, pageSize),
            detail: id => client.caseDetail(id) });
    }
    function createDemoAdapter(records) {
        return Object.freeze({ mode: "demo", async list() { return records.map(record => ({ ...record })); } });
    }
    function errorView(error) {
        const status = Number(error?.status) || 0;
        const messages = { 401: "登入狀態已失效，請重新登入。", 403: "您沒有存取此個案資料的權限。",
            404: "找不到指定個案，或此個案目前不可見。", 503: "個案服務暫時無法使用，請稍後再試。" };
        return Object.freeze({ status, message: messages[status] || (error?.code === "INVALID_RESPONSE" ?
            "個案服務回應格式異常，請稍後再試。" : "無法讀取個案資料，請稍後再試。") });
    }
    function selectRows(rows, { search = "", status = "all", sort = "default", mode = "demo" } = {}) {
        const query = search.trim().toLocaleLowerCase();
        return rows.filter(row => row.name.toLocaleLowerCase().includes(query) || (row.code || row.id).toLocaleLowerCase().includes(query))
            .filter(row => status === "all" || row.status === status)
            .sort((a, b) => {
                if (sort === "code") return (a.code || a.id).localeCompare(b.code || b.id);
                if (mode === "demo" && sort === "default" && status !== "completed") {
                    const priority = Number(b.priority === "high") - Number(a.priority === "high");
                    if (priority) return priority;
                }
                return sort === "oldest" ? a.updatedAt.localeCompare(b.updatedAt) : b.updatedAt.localeCompare(a.updatedAt);
            });
    }
    function createStore(adapter, { onChange = () => {}, canRead = () => true } = {}) {
        let revision = 0;
        let state = { phase: "loading", rows: [], detail: null, error: null, page: 1, pageSize: 10, hasNext: false, mode: adapter.mode };
        const snapshot = () => ({ ...state, rows: state.rows.map(row => ({ ...row })), detail: state.detail ? { ...state.detail } : null });
        function publish(patch) { state = { ...state, ...patch }; onChange(snapshot()); }
        function clearData() { publish({ phase: "loading", rows: [], detail: null, hasNext: false, error: null }); }
        function invalidate() { revision++; publish({ phase: "loading", rows: [], detail: null, hasNext: false, error: null }); }
        function fail(error) { revision++; publish({ phase: "error", rows: [], detail: null, hasNext: false, error: errorView(error) }); }
        async function load(kind, value, pageSize = 10) {
            const current = ++revision;
            publish({ phase: "loading", rows: [], detail: null, hasNext: false, error: null,
                ...(kind === "list" ? { page: value, pageSize } : {}) });
            try {
                if (!canRead()) throw new api.MedicalApiError(401, "SESSION_REQUIRED");
                if (kind === "detail" && !api.uuid(value)) throw new api.MedicalApiError(404, "CASE_NOT_FOUND");
                const result = kind === "list" ? await adapter.list(value, pageSize) : await adapter.detail(value);
                if (current !== revision) return;
                if (!canRead()) throw new api.MedicalApiError(401, "SESSION_REQUIRED");
                if (kind === "detail") {
                    const detail = toViewModel(result);
                    if (detail.id !== value) throw new api.MedicalApiError(0, "INVALID_RESPONSE");
                    publish({ phase: "ready", detail });
                } else {
                    if (!Array.isArray(result) || (adapter.mode === "api" && result.length > pageSize)) throw new api.MedicalApiError(0, "INVALID_RESPONSE");
                    const rows = adapter.mode === "api" ? result.map(toViewModel) : result;
                    if (new Set(rows.map(row => row.id)).size !== rows.length) throw new api.MedicalApiError(0, "INVALID_RESPONSE");
                    publish({ phase: rows.length ? "ready" : "empty", rows, hasNext: adapter.mode === "api" && rows.length === pageSize });
                }
            } catch (error) {
                if (current === revision) publish({ phase: "error", rows: [], detail: null, hasNext: false, error: errorView(error) });
            }
        }
        return { snapshot, clearData, invalidate, fail, list: (page = 1, pageSize = 10) => load("list", page, pageSize), detail: id => load("detail", id) };
    }
    return { route, labels, missing, toViewModel, createApiAdapter, createDemoAdapter, selectRows, createStore };
});
