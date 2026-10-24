(function (root, factory) {
    const model = factory(typeof module === "object" && module.exports ? require("./medical-api.js") : root.MedicalApi);
    if (typeof module === "object" && module.exports) module.exports = model;
    else root.MedicalTasks = model;
})(typeof window === "undefined" ? globalThis : window, function (api) {
    "use strict";
    const labels = Object.freeze({ PENDING: "待填", IN_PROGRESS: "進行中", SUBMITTED: "已提交", CANCELLED: "已取消" });
    const formatTime = value => value === null ? "未提供" : new Date(value).toLocaleString("zh-TW", { timeZone: "Asia/Taipei" });
    function toViewModel(dto, caseId) {
        const t = api.taskDto(dto, caseId);
        return Object.freeze({ taskId: t.taskId, caseId: t.caseId, questionnaireVersionId: t.questionnaireVersionId,
            title: t.questionnaireTitle, version: t.versionNumber, role: { PARENT: "家長", TEACHER: "教師" }[t.respondentRole],
            status: t.taskStatus, statusLabel: labels[t.taskStatus], round: t.assignmentRound,
            required: t.isRequired ? "必要" : "非必要", submittedAt: formatTime(t.submittedAtUtc), result: "未提供" });
    }
    const createApiAdapter = client => Object.freeze({ list: id => client.tasks(id), completion: id => client.taskCompletion(id) });
    function errorView(error) {
        const status = Number(error?.status) || 0;
        const messages = { 401: "登入狀態已失效，請重新登入。", 403: "您沒有存取此個案問卷的權限。",
            404: "找不到指定個案或問卷紀錄。", 503: "問卷服務暫時無法使用，請稍後再試。" };
        return { status, message: messages[status] || (error?.code === "INVALID_RESPONSE" ?
            "問卷服務回應格式異常，請稍後再試。" : "無法讀取問卷紀錄，請稍後再試。") };
    }
    function createStore(adapter, { onChange = () => {}, canRead = () => true } = {}) {
        let revision = 0;
        const empty = caseId => ({ caseId, phase: "loading", rows: [], error: null,
            completionPhase: "loading", completion: null, completionError: null });
        let state = empty(null);
        const snapshot = () => ({ ...state, rows: state.rows.map(row => ({ ...row })),
            completion: state.completion ? { ...state.completion } : null });
        const publish = patch => { state = { ...state, ...patch }; onChange(snapshot()); };
        function invalidate() { revision++; state = empty(null); onChange(snapshot()); }
        function clearData() { publish({ ...empty(state.caseId) }); }
        function unauthorized(cause = { status: 401 }) {
            revision++; const error = errorView(cause);
            publish({ phase: "error", rows: [], error, completionPhase: "error", completion: null, completionError: error });
        }
        async function load(caseId) {
            const current = ++revision;
            state = empty(caseId); onChange(snapshot());
            if (!canRead()) { unauthorized(); return; }
            if (!api.uuid(caseId)) {
                const error = errorView({ status: 404 });
                publish({ phase: "error", error, completionPhase: "error", completionError: error }); return;
            }
            async function read(kind) {
                try {
                    const value = await (kind === "list" ? adapter.list(caseId) : adapter.completion(caseId));
                    if (current !== revision) return;
                    if (!canRead()) { unauthorized(); return; }
                    if (kind === "list") {
                        if (!Array.isArray(value)) throw new api.MedicalApiError(0, "INVALID_RESPONSE");
                        const rows = value.map(t => toViewModel(t, caseId));
                        if (new Set(rows.map(t => t.taskId)).size !== rows.length) throw new api.MedicalApiError(0, "INVALID_RESPONSE");
                        publish({ phase: rows.length ? "ready" : "empty", rows });
                    } else publish({ completionPhase: "ready", completion: api.completionDto(value, caseId) });
                } catch (error) {
                    if (current !== revision) return;
                    if (!canRead() || error?.status === 401) { unauthorized(error?.status !== undefined ? error : { status: 401 }); return; }
                    if (kind === "list") publish({ phase: "error", rows: [], error: errorView(error) });
                    else publish({ completionPhase: "error", completion: null, completionError: errorView(error) });
                }
            }
            await Promise.all([read("list"), read("completion")]);
        }
        return { snapshot, load, invalidate, clearData };
    }
    return { labels, toViewModel, createApiAdapter, createStore };
});
