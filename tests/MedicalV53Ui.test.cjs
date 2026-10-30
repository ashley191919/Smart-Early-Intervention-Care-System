'use strict';
// Isolated UI acceptance: no real sessions, API Program, storage or database.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
class Element {
    constructor() { this.children = []; this.hidden = false; this.disabled = false; this.attrs = {}; this.events = {}; this.value = ''; }
    set textContent(value) { this.text = String(value); this.children = []; }
    get textContent() { return (this.text || '') + this.children.map(c => c.textContent).join(''); }
    set innerHTML(_) { throw new Error('Unsafe HTML insertion'); }
    setAttribute(k, v) { this.attrs[k] = v; }
    getAttribute(k) { return this.attrs[k]; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.text = ''; this.children = children; }
    addEventListener(name, handler) { this.events[name] = handler; }
}
function responsePage(config, search) {
    const nodes = new Map([...read('response-detail.html').matchAll(/id="([^"]+)"/g)].map(m => [m[1], new Element()]));
    let reads = 0;
    const window = { MedicalApiConfig: config, MedicalApi: api, SharedDemo: { tasks() { reads++; throw new Error('Demo must not be read'); } },
        MedicalDemo: { resolve() { reads++; throw new Error('Fixture must not be read'); } }, addEventListener() {} };
    const document = { getElementById: id => nodes.get(id), createElement: () => new Element(), addEventListener() {} };
    vm.runInNewContext(read('js/response-detail.js'), { window, document, location: { search }, URLSearchParams, Intl });
    return { nodes, reads };
}
for (const [config, search] of [
    [{ caseSource: 'api' }, '?source=medical-demo&caseId=DEMO-001&taskId=medical-demo-snap-teacher-001-v1'],
    [{ caseSource: 'demo' }, '?source=api&caseId=DEMO-001&taskId=anything'],
    [{ caseSource: 'demo' }, '?caseId=20000000-0000-4000-8000-000000000001&taskId=anything']
]) test('formal response entry never falls back to Demo: ' + search, () => {
    const page = responsePage(config, search);
    assert.equal(page.reads, 0);
    assert.equal(page.nodes.get('response-state').textContent, '尚未提供正式結果');
    assert.equal(page.nodes.get('response-record').hidden, true);
    assert.equal(page.nodes.get('response-questions').children.length, 0);
    assert.match(page.nodes.get('response-back-top').href, /source=api/);
});
for (const [config, search] of [[{ caseSource: 'api' }, ''], [{ caseSource: 'demo' }, '?source=api']])
test('Dashboard official mode uses neither fixed statistics nor fixture tasks: ' + JSON.stringify(config), () => {
    const selectors = new Map(['#todo-list', '.todo-result-count', '.todo-filter-hint', '#todo-date-reference', '.todo-empty'].map(s => [s, new Element()]));
    selectors.get('#todo-list').append(new Element());
    const values = Array.from({ length: 4 }, () => new Element()), buttons = Array.from({ length: 4 }, () => new Element());
    const panel = { querySelector: s => selectors.get(s), querySelectorAll: () => buttons };
    const document = { querySelector: () => panel, querySelectorAll: () => values };
    vm.runInNewContext(read('js/staff-dashboard.js'), { window: { MedicalApiConfig: config }, document, location: { search }, URLSearchParams });
    assert.deepEqual(values.map(v => v.textContent), ['未提供', '未提供', '未提供', '未提供']);
    assert.equal(selectors.get('#todo-list').children.length, 0);
    assert.equal(selectors.get('.todo-empty').hidden, false);
    assert(buttons.every(b => b.disabled));
});
for (const search of ['', '?caseId=20000000-0000-4000-8000-000000000001'])
test('formal assignment clears fixed child and does not access fixture storage: ' + search, () => {
    const form = new Element(), main = new Element();
    let writes = 0;
    const document = { querySelector: selector => selector === '#form-assign-form' ? form : main, createElement: () => new Element() };
    const window = { MedicalApiConfig: { caseSource: 'api' }, MedicalApi: api };
    vm.runInNewContext(read('js/form-assign.js'), { window, document, location: { search }, URLSearchParams,
        SharedDemo: { assign() { writes++; } } });
    assert.equal(writes, 0);
    assert.equal(form.events.submit, undefined);
    assert.equal(main.children.length, 2);
    assert.match(main.children[0].href, /source=api/);
    assert(!main.textContent.includes('王○安') && !main.textContent.includes('林○樂'));
});
test('assignment requires a registered Permission, not merely a role or read Permission', () => {
    const fields = new Map(['#assign-forms-group', '#assign-forms-error', '#assign-selected-count', '#assign-parent-count',
        '#assign-teacher-count', '#assignmentDeadline', '#assign-deadline-error', '#assign-feedback',
        '#assign-invitation-demo', '#assign-confirm'].map(s => [s, new Element()]));
    const form = new Element(); form.querySelector = s => fields.get(s); form.querySelectorAll = () => [];
    let writes = 0, checked = null;
    vm.runInNewContext(read('js/form-assign.js'), { document: { querySelector: () => form },
        window: { MedicalApiConfig: { caseSource: 'demo' }, StaffAuth: { hasPermission(p) { checked = p; return false; } } },
        location: { search: '' }, URLSearchParams, SharedDemo: { assign() { writes++; } } });
    let prevented = false;
    form.events.submit({ preventDefault() { prevented = true; } });
    assert(prevented); assert.equal(checked, 'questionnaire.task.assign');
    assert.equal(writes, 0); assert.equal(fields.get('#assign-confirm').disabled, true);
    assert.equal(fields.get('#assign-feedback').textContent, '您沒有指派問卷的權限。');
});
test('assignment uses existing Session/Permission presenters; presets stay unset and exports stay disabled', () => {
    const html = read('form-assign.html');
    assert(html.indexOf('js/medical-config.js') < html.indexOf('js/staff-auth.js'));
    assert(html.includes('data-staff-protected hidden'));
    assert.match(html, /disabled data-staff-permission="questionnaire.task.assign"/);
    assert.match(html, /button[^>]*disabled[^>]*>套用常用問卷/);
    assert(!/<input[^>]*type="checkbox"[^>]*\schecked\b/.test(html));
    const response = read('response-detail.html');
    for (const format of ['Excel', 'Word']) assert.match(response, new RegExp('<button[^>]*disabled[^>]*>匯出 ' + format));
    assert(!/download=/.test(response));
});
test('API Header keeps navigation in API mode and never exposes fixture reminders', () => {
    for (const search of ['?source=api', '?caseId=20000000-0000-4000-8000-000000000001']) {
        const overview = new Element(), cases = new Element(), create = new Element();
        overview.attrs.href = 'staff-dashboard.html'; cases.attrs.href = 'cases.html';
        create.remove = () => { create.removed = true; };
        const header = { dataset: {}, querySelector: () => null };
        const document = { querySelector: () => header, querySelectorAll: selector => selector.includes('case-create') ? [create] : [overview, cases] };
        vm.runInNewContext(read('js/staff-header.js'), { document, window: { MedicalApiConfig: { caseSource: 'demo' } }, location: { search }, URLSearchParams });
        assert.equal(overview.href, 'staff-dashboard.html?source=api');
        assert.equal(cases.href, 'cases.html?source=api');
        assert(create.removed);
    }
    const source = read('js/staff-header.js');
    const reminderSource = source.slice(source.indexOf('    function reminderSource()'), source.indexOf('    function refreshReminders()'));
    const result = vm.runInNewContext(reminderSource + ';reminderSource();', {
        apiMode: true, window: {}, document: { querySelectorAll() { throw new Error('No fixture access in API mode'); } }
    });
    assert.equal(result.length, 0);
});
