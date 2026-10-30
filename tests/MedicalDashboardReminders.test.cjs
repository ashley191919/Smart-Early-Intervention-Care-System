'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const model = require(path.join(root, 'js/staff-dashboard.js'));
const cases = require(path.join(root, 'js/case-demo-data.js'));
function records() {
    return [...read('staff-dashboard.html').matchAll(/<li class="todo-item"([\s\S]*?)<\/li>/g)].map((match, originalIndex) => {
        const attrs = Object.fromEntries([...match[1].matchAll(/data-([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
        return { id: attrs['todo-id'], caseId: attrs['case-id'], caseStatus: attrs['case-status'],
            assessmentDate: attrs['assessment-date'], status: attrs['todo-status'], updatedAt: attrs['updated-at'],
            priority: attrs['case-priority'], taskPriority: attrs.priority, originalIndex, html: match[0],
            title: match[1].match(/<h3>([^<]*)<\/h3>/)[1],
            reminder: match[1].match(/class="todo-reminder-value">([^<]*)<\/span>/)[1] };
    });
}
class Element {
    constructor(text = '') { this.textContent = text; this.dataset = {}; this.children = []; this.attrs = {}; this.events = {}; this.hidden = false; this.disabled = false;
        this.classList = { toggle: (name, value) => { this.selected = value; } }; }
    setAttribute(name, value) { this.attrs[name] = value; }
    addEventListener(name, handler) { this.events[name] = handler; }
    appendChild(child) { this.children = this.children.filter(c => c !== child); this.children.push(child); }
    replaceChildren(...children) { this.children = children; }
    click() { this.events.click?.(); }
}
function mount({ api = false, search = '', mutate = () => {} } = {}) {
    const values = [24, 5, 8, 11].map(x => new Element(String(x)));
    const data = records(); mutate(data);
    const rows = data.map(item => {
        const row = new Element(); row.dataset = { todoStatus: item.status, priority: item.taskPriority, casePriority: item.priority,
            updatedAt: item.updatedAt, caseStatus: item.caseStatus, assessmentDate: item.assessmentDate };
        row.id = item.id;
        row.fields = Object.fromEntries(['.todo-assessment', '.todo-assessment-date', '.todo-case-status',
            '.todo-updated-at', '.todo-priority', '.todo-normal-priority'].map(s => [s, new Element()]));
        row.querySelector = s => row.fields[s]; return row;
    });
    const makeButtons = (items, key) => items.map(([value, text]) => { const b = new Element(text); b.dataset[key] = value; return b; });
    const statuses = makeButtons([['all', '全部'], ['pending-contact', '待聯繫'], ['in-progress', '進行中'], ['completed', '已完成']], 'filterCaseStatus');
    const list = new Element(); list.children = [...rows]; list.querySelectorAll = () => rows;
    const count = new Element(), empty = new Element(), hint = new Element();
    const fields = { '#todo-list': list, '.todo-result-count': count, '.todo-empty': empty, '.todo-filter-hint': hint };
    const panel = { querySelector: s => fields[s], querySelectorAll: () => statuses };
    const document = { querySelector: () => panel, querySelectorAll: () => values };
    vm.runInNewContext(read('js/staff-dashboard.js'), { document, window: { MedicalApiConfig: { caseSource: api ? 'api' : 'demo' } },
        location: { search }, URLSearchParams });
    return { rows, statuses, count, empty, list, values, visible: () => list.children.filter(row => !row.hidden) };
}
test('five reminder phrases remain content, with one status filter and no classification UI', () => {
    const page = mount();
    for (const phrase of ['初次通知', '評估前一個月', '評估前一週', '評估前一天', '評估日'])
        assert(records().some(r => r.reminder === phrase));
    assert(records().filter(r => r.reminder === '未提供').length > 0);
    const html = read('staff-dashboard.html');
    assert.equal((html.match(/class="todo-filters"/g) || []).length, 1);
    assert.doesNotMatch(html, /data-filter-type|data-reminder-node|todo-reminder-fields|todo-overdue|todo-date-reference/);
    const missing = page.rows.find(r => r.id === 'TODO-006');
    assert.equal(missing.fields['.todo-assessment'].hidden, true);
    assert.equal(missing.fields['.todo-assessment-date'].textContent, '');
    const sample = page.rows.find(r => r.id === 'TODO-002');
    assert.equal(sample.fields['.todo-assessment-date'].textContent, '2026/10/15');
    assert.equal(sample.dataset.caseStatus, 'in-progress');
});
test('Demo case states agree with Case list; existing task priorities and statuses are independent', () => {
    for (const item of records()) {
        const child = cases.find(c => c.id === item.caseId);
        assert.equal(item.caseStatus, child.status);
        assert.equal(item.updatedAt, child.updatedAt);
        assert.equal(item.priority, child.priority);
    }
    assert.deepEqual(records().map(r => r.taskPriority), ['normal', 'high', 'normal', 'high', 'normal', 'normal', 'normal', 'normal']);
    const page = mount({ mutate: items => { items[0].assessmentDate = '2026-12-01'; } });
    assert.equal(page.rows[0].fields['.todo-case-status'].textContent, '待聯繫');
    assert.equal(page.rows[0].dataset.todoStatus, 'pending');
});
test('single case status filter updates counts/aria and handles empty state without changing data', () => {
    const page = mount(), before = JSON.stringify(page.rows.map(r => r.dataset));
    page.statuses[1].click(); assert.equal(page.visible().length, 2);
    assert(page.visible().every(r => r.dataset.caseStatus === 'pending-contact'));
    page.statuses[2].click(); assert.equal(page.visible().length, 5);
    page.statuses[3].click(); assert.equal(page.visible().length, 1);
    assert.match(page.count.textContent, /1 筆提醒/); assert.equal(page.statuses[3].attrs['aria-pressed'], 'true');
    assert.equal(JSON.stringify(page.rows.map(r => r.dataset)), before);
    const empty = mount({ mutate: rows => rows.forEach(r => { r.caseStatus = 'in-progress'; }) });
    empty.statuses[3].click(); assert.equal(empty.visible().length, 0); assert.equal(empty.empty.hidden, false);
    empty.statuses[0].click(); assert.equal(empty.empty.hidden, true);
});
test('absent/invalid assessment dates stay hidden; valid dates are only displayed, not inferred', () => {
    const page = mount({ mutate: rows => { rows[0].assessmentDate = '2026-02-30'; } });
    assert.equal(page.rows[0].fields['.todo-assessment-date'].textContent, '');
    for (const id of ['TODO-001', 'TODO-006', 'TODO-007'])
        assert.equal(page.rows.find(r => r.id === id).fields['.todo-assessment'].hidden, true);
    const valid = page.rows.find(r => r.id === 'TODO-002');
    assert.equal(valid.fields['.todo-assessment'].hidden, false);
    assert.equal(valid.fields['.todo-assessment-date'].attrs.datetime, '2026-10-15');
    for (const date of ['', null, undefined, '2026-2-03', '2026-02-29', '2026-04-31', 'updatedAtUtc']) assert.equal(model.calendarDate(date), '');
    assert.equal(model.calendarDate('2024-02-29'), '2024-02-29');
});
function item(id, updatedAt, assessmentDate = '', priority = 'normal', originalIndex = 0) {
    return { id, updatedAt, assessmentDate, priority, originalIndex };
}
test('high priority then newest update; assessment dates have no effect on sorting', () => {
    const rows = [item('high-later', '2026-10-01', '2026-12-01', 'high', 0),
        item('high-sooner', '2026-10-02', '2026-11-01', 'high', 1),
        item('normal', '2026-09-01', '2026-10-01', 'normal', 2)];
    const before = JSON.stringify(rows);
    assert.deepEqual(model.sortReminders(rows).map(r => r.id), ['high-sooner', 'high-later', 'normal']);
    assert.deepEqual(model.sortReminders(rows.map(r => ({ ...r, assessmentDate: '1900-01-01' }))).map(r => r.id), ['high-sooner', 'high-later', 'normal']);
    assert.equal(JSON.stringify(rows), before);
});
test('equal updates stay stable and missing assessment dates never change ordering', () => {
    const rows = [item('missing', '2026-10-01', '', 'normal', 0), item('later', '2026-10-02', '2026-12-01', 'normal', 1),
        item('sooner', '2026-10-03', '2026-11-01', 'normal', 2), item('tie', '2026-10-03', '2026-11-01', 'normal', 3)];
    assert.deepEqual(model.sortReminders(rows).map(r => r.id), ['sooner', 'tie', 'later', 'missing']);
    assert.deepEqual(model.sortReminders(rows.map(r => ({ ...r, assessmentDate: '' }))).map(r => r.id), ['sooner', 'tie', 'later', 'missing']);
    assert.deepEqual(mount().list.children.map(r => r.id), ['TODO-002', 'TODO-005', 'TODO-004', 'TODO-001', 'TODO-003', 'TODO-006', 'TODO-007', 'TODO-008']);
});
test('formal mode clears every Demo row and disables status filters without fallback', () => {
    for (const options of [{ api: true }, { search: '?source=api' }, { search: '?source=invalid' }, { search: '?caseId=invalid' }]) {
        const page = mount(options);
        assert.equal(page.list.children.length, 0);
        assert.equal(page.empty.textContent, '提醒資料尚未提供');
        assert.equal(page.empty.hidden, false);
        assert(page.statuses.every(b => b.disabled));
        assert.deepEqual(page.values.map(v => v.textContent), ['未提供', '未提供', '未提供', '未提供']);
        // Early return: static Demo node/date fields were never read/rendered.
        assert(page.rows.every(r => r.fields['.todo-case-status'].textContent === ''));
    }
});
test('Dashboard remains on existing scripts; no notifications, persistence, date offsets or Case workflow writes', () => {
    const source = read('js/staff-dashboard.js'), html = read('staff-dashboard.html');
    assert.doesNotMatch(source, /fetch\(|localStorage|sessionStorage|setInterval\(|setTimeout\(|updatedAtUtc|\.setMonth\(/);
    assert.equal((html.match(/data-filter-case-status=/g) || []).length, 4);
    assert.equal((html.match(/data-filter-type=/g) || []).length, 0);
    assert.doesNotMatch(source, /selectedType|reminderNodes|isOverdue|assessmentDate\.localeCompare/);
    for (const row of records()) {
        const links = [...row.html.matchAll(/href="([^"]+)"/g)].map(m => m[1]);
        assert.deepEqual(links, row.caseId === 'DEMO-001' ? ['case-detail.html'] : row.caseId === 'DEMO-024' ? ['case-detail-empty.html'] : []);
        assert.doesNotMatch(row.html, /class="todo-status"|todo-reminder-fields/);
        assert(row.html.includes('<small class="todo-case-code">' + row.caseId + '</small>'));
        assert(row.html.includes('class="todo-updated-at"'));
        assert(row.html.includes('class="todo-priority"'));
    }
    assert.match(html, /dashboard-stat-value">24/);
    assert.doesNotMatch(html, /<button[^>]*onclick/);
});
test('original tracking titles/Case codes remain; teacher reminders are additional content', () => {
    const originalTitles = ['首次聯絡家長', '確認早療評估安排', '再次聯絡家長',
        '確認後續預約安排', '提醒家長完成表單', '首次聯絡家長'];
    assert.deepEqual(records().slice(0, 6).map(r => r.title), originalTitles);
    for (const id of ['DEMO-001', 'DEMO-002', 'DEMO-003', 'DEMO-004', 'DEMO-005', 'DEMO-006'])
        assert(records().some(r => r.caseId === id), 'Original Case missing: ' + id);
    assert(records().some(r => r.caseId === 'DEMO-024'), 'Keep existing empty-case route too');
});
test('visible priority and update fields are restored; completed filter uses update dates only', () => {
    const page = mount();
    page.rows.forEach(row => {
        assert.equal(row.fields['.todo-updated-at'].textContent, row.dataset.updatedAt.replaceAll('-', '/'));
        assert.equal(row.fields['.todo-updated-at'].attrs.datetime, row.dataset.updatedAt);
        assert.equal(row.fields['.todo-priority'].hidden, row.dataset.casePriority !== 'high');
        assert.equal(row.fields['.todo-normal-priority'].hidden, row.dataset.casePriority === 'high');
    });
    const rows = [item('old-high', '2026-10-01', '', 'high', 0), item('new-normal', '2026-10-05', '', 'normal', 1)];
    assert.deepEqual(model.sortReminders(rows, 'completed').map(r => r.id), ['new-normal', 'old-high']);
    const missing = mount({ mutate: rows => { rows[0].updatedAt = ''; } });
    assert.equal(missing.rows[0].fields['.todo-updated-at'].textContent, '未提供');
});
