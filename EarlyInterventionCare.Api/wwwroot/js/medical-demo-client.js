(() => {
    'use strict';
    // Transport and navigation only. Synthetic questions/answers are NOT shipped in this file.
    const taskId = 'medical-demo-snap-teacher-001-v1';
    const label = 'Demo 模擬資料';
    const prefix = '/api/medical-demo';
    const single = (params, name) => params.getAll(name).length === 1 ? params.get(name) : null;
    const enabled = search => single(new URLSearchParams(search), 'medicalDemo') === '1';
    function responseHref(entry, search) {
        const params = new URLSearchParams({ taskId: entry.task.id, caseId: entry.task.caseId, source: entry.source });
        return '/response-detail.html?' + params;
    }
    function caseHref(caseId, search) {
        const routes = { 'DEMO-001': 'case-detail.html', 'DEMO-024': 'case-detail-empty.html' };
        return Object.hasOwn(routes, caseId) ? routes[caseId] + '#shared-tasks' : 'cases.html';
    }
    function createClient({ request, getSession, expired = () => {} }) {
        let generation = 0;
        let expiredNotified = false;
        function invalidate() { generation++; }
        async function get(path) {
            const user = getSession(), revision = generation;
            if (!user) throw new Error('Medical session required');
            const response = await request(prefix + path, { method: 'GET', credentials: 'same-origin',
                cache: 'no-store', headers: { Accept: 'application/json' } });
            if (revision !== generation || getSession()?.userId !== user.userId) throw new Error('Stale demo request');
            if (response.status === 401) {
                invalidate();
                if (!expiredNotified) { expiredNotified = true; expired(); }
                throw new Error('Medical session expired');
            }
            if (!response.ok) throw Object.assign(new Error('Medical demo unavailable'), { status: response.status });
            const body = await response.json();
            if (revision !== generation || getSession()?.userId !== user.userId) throw new Error('Stale demo request');
            return body;
        }
        async function availability() {
            const body = await get('/availability');
            return body?.enabled === true && body.label === label;
        }
        async function records(localTasks, caseId, search) {
            if (!Array.isArray(localTasks)) throw new Error('Invalid local tasks');
            const local = localTasks.filter(t => t && t.caseId === caseId).map(task => ({ task, source: 'local-demo' }));
            if (!['DEMO-001', 'DEMO-024'].includes(caseId)) return local;
            let tasks;
            try { tasks = await get('/cases/' + caseId + '/tasks'); }
            catch (error) {
                // Production has no Demo surface. Preserve existing local records, never synthesize answers.
                if (error.status === 404) return local;
                throw error;
            }
            if (!Array.isArray(tasks) || tasks.some(t => t?.caseId !== caseId || t.id !== taskId || t.isDemo !== true))
                throw new Error('Invalid demo list');
            return local.concat(tasks.map(task => ({ task, source: 'medical-demo' })));
        }
        async function resolve(localTasks, search) {
            const params = new URLSearchParams(search), id = single(params, 'taskId'), caseId = single(params, 'caseId');
            if (!id || (params.has('caseId') && !caseId)) return null;
            const source = params.has('source') ? single(params, 'source') : 'local-demo';
            if (source === 'medical-demo') {
                // The URL identifies a record; only the server grants access. Keep legacy flag validation.
                if ((params.has('medicalDemo') && !enabled(search)) || caseId !== 'DEMO-001' || id !== taskId) return null;
                const task = await get('/cases/DEMO-001/tasks/' + taskId);
                if (task?.caseId !== caseId || task.id !== id || task.isDemo !== true || task.dataSource !== source)
                    throw new Error('Invalid demo response');
                return { task, source };
            }
            if (source !== 'local-demo' || !Array.isArray(localTasks)) return null;
            const matches = localTasks.filter(t => t && t.id === id);
            if (matches.length !== 1 || (caseId && matches[0].caseId !== caseId)) return null;
            return { task: matches[0], source };
        }
        return Object.freeze({ taskId, label, enabled, responseHref, caseHref, records, resolve, availability, invalidate });
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { taskId, label, enabled, responseHref, caseHref, createClient };
    if (typeof window === 'undefined' || typeof document === 'undefined') return;
    const client = createClient({ request: (...args) => fetch(...args), getSession: () => window.StaffAuth?.getCurrentUser(),
        expired: () => { revoke(); void window.StaffAuth?.requireStaffSession(); } });
    window.MedicalDemo = client;
    function revoke() {
        client.invalidate();
        document.dispatchEvent(new Event('medical-demo-invalidated'));
    }
    document.addEventListener('staff-authenticated', revoke);
    document.addEventListener('staff-permissions-changed', () => { if (!window.StaffAuth?.getCurrentUser()) revoke(); });
    window.addEventListener('pagehide', revoke);
})();
