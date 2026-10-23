const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const { buildFixtureView, buildScoreView, formatTime } = require('../EarlyInterventionCare.Api/wwwroot/js/response-detail.js');
const medicalClient = require('../EarlyInterventionCare.Api/wwwroot/js/medical-demo-client.js');
const serverFixture = JSON.parse(fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/Development/MedicalDemoFixture.json'), 'utf8'));

function task(formId = 'snap', role = 'parent') {
    const count = formId === 'snap' ? 26 : 14;
    return { id: 'response-ui-' + formId, caseId: 'DEMO-001', formId, role, name: '測試問卷', version: 'fixture-1', status: 'done',
        draft: { submittedAt: '2026-10-19T02:30:00Z', note: '第一行\n第二行',
            questions: Array.from({ length: count }, (_, i) => '測試題目 ' + (i + 1)), options: ['選項零', '選項一'],
            answers: Object.fromEntries(Array.from({ length: count }, (_, i) => [i, i % 2])) } };
}

test('no score source means unscored, never zero or a diagnosis', () => {
    const score = buildScoreView(null, true);
    assert.equal(score.status, '尚未計分'); assert.deepEqual(score.cards, []); assert.deepEqual(score.meta, []);
});
test('unfinished responses suppress even a supplied score result', () => {
    assert.deepEqual(buildScoreView({ status: 'SCORED', total: { value: 999 } }, false).cards, []);
    assert.equal(buildScoreView(null, false).status, '未完成');
});
test('presentation accepts an optional total of zero without computing it', () => {
    const input = { status: 'SCORED', total: { value: 0, unit: '測試單位' }, method: '測試方法', version: 'test-only' };
    const before = JSON.stringify(input), view = buildScoreView(input, true);
    assert.equal(view.cards.length, 1); assert.equal(view.cards[0].value, 0);
    assert.deepEqual(view.meta, [['計分方式', '測試方法'], ['計分版本', 'test-only']]);
    assert.equal(JSON.stringify(input), before);
});
test('subscale-only results do not invent a total', () => {
    const view = buildScoreView({ status: 'SCORED', subscales: [{ label: '測試分項', value: 7 }] }, true);
    assert.equal(view.status, '已計分'); assert.equal(view.cards.length, 1); assert.equal(view.cards[0].total, false);
});
test('total and variable subscale counts use only supplied values', () => {
    const view = buildScoreView({ status: 'SCORED', total: { value: 18 }, subscales: [{ label: '測試甲', value: 4 }, { label: '測試乙', value: 5 }] }, true);
    assert.deepEqual(view.cards.map(c => c.value), [18, 4, 5]);
});
test('not applicable is distinct from waiting for scoring', () => {
    const view = buildScoreView({ status: 'NOT_APPLICABLE', total: { value: 18 } }, true);
    assert.equal(view.status, '不適用'); assert.deepEqual(view.cards, []);
});
test('malformed or unconfirmed scores never appear as numeric results', () => {
    for (const result of [{ total: { value: 9 } }, { status: 'PENDING', total: { value: 9 } },
        { status: 'SCORED', total: { value: '9' } }, { status: 'SCORED', total: { value: NaN } },
        { status: 'SCORED', subscales: [{ label: '', value: 5 }, { label: '測試', value: Infinity }] }]) {
        assert.equal(buildScoreView(result, true).status, '尚未計分');
    }
});
for (const [formId, count] of [['snap', 26], ['clancy', 14]]) {
    for (const role of ['parent', 'teacher']) {
        test(`${formId}/${role} preserves all ${count} questions and labels, without scoring local answers`, () => {
            const original = task(formId, role); original.scoring = { status: 'SCORED', total: { value: 999 } };
            original.draft.totalScore = 999;
            const before = JSON.stringify(original), view = buildFixtureView(original);
            assert.equal(view.completed, true); assert.equal(view.questions.length, count);
            assert.equal(view.questions.at(-1).number, count); assert.equal(view.questions[0].answer, '選項零');
            assert.equal(view.scoring.status, '尚未計分'); assert.deepEqual(view.scoring.cards, []);
            assert.equal(view.role, role === 'parent' ? '家長' : '教師');
            assert.equal(view.caseName, '王○安'); assert.equal(view.observation, '第一行\n第二行');
            assert.equal(JSON.stringify(original), before);
        });
    }
}
test('pending, drafts, missing receipts, and invalid times do not show completed results', () => {
    for (const status of ['pending', 'draft', 'cancelled']) {
        const input = task(); input.status = status; assert.equal(buildFixtureView(input).completed, false);
    }
    for (const submittedAt of [null, undefined, '', 'invalid', false, {}]) {
        const input = task(); input.draft.submittedAt = submittedAt; assert.equal(buildFixtureView(input).completed, false);
    }
});
test('missing question snapshots or labels use explicit fallback text, never new clinical content', () => {
    const input = task(); input.draft.questions = []; input.draft.options = []; input.draft.answers = { 2: 1 };
    const view = buildFixtureView(input);
    assert.equal(view.questions[0].number, 3); assert.equal(view.questions[0].text, '題目內容未提供');
    assert.match(view.questions[0].answer, /選項代碼 1/);
});
test('timestamps display in Taiwan time and absent observation is explicit', () => {
    assert.match(formatTime('2026-10-19T02:30:00Z'), /2026\/10\/19.*10:30/);
    const input = task(); input.draft.note = '  ';
    assert.equal(buildFixtureView(input).observation, '未填寫補充觀察');
});

async function mount(tasks, search = '?taskId=response-ui-snap', readTasks = () => tasks) {
    class Element {
        constructor() { this.children = []; this.hidden = false; this.className = ''; this.value = ''; }
        set textContent(value) { this.value = String(value); this.children = []; }
        get textContent() { return this.value + this.children.map(c => c.textContent).join(''); }
        set innerHTML(_) { throw new Error('User content must be text nodes'); }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.value = ''; this.children = children; }
    }
    const elements = new Map([...read('response-detail.html').matchAll(/id="([^"]+)"/g)].map(m => [m[1], new Element()]));
    const handlers = new Map();
    const document = { getElementById: id => elements.get(id), createElement: () => new Element(), title: '',
        addEventListener: (name, cb) => handlers.set(name, cb) };
    const window = { SharedDemo: { tasks: readTasks }, addEventListener: (name, cb) => handlers.set(name, cb) };
    const getSession = () => ({ userId: 'test-staff' });
    window.StaffAuth = { getCurrentUser: getSession };
    window.MedicalDemo = medicalClient.createClient({ getSession, request: async () => ({ ok: true, status: 200, json: async () => serverFixture }) });
    vm.runInNewContext(read('js/response-detail.js'), { document, window, location: { search }, URLSearchParams, Intl });
    await new Promise(setImmediate);
    return { elements, document, handlers };
}
test('actual renderer safely displays every answer and keeps export disabled in HTML', async () => {
    const input = task(); input.draft.questions[0] = '<img src=x onerror=alert(1)>';
    const page = await mount([input]);
    assert.equal(page.elements.get('response-record').hidden, false);
    assert.equal(page.elements.get('response-questions').children.length, 26);
    assert.ok(page.elements.get('response-questions').children[0].textContent.includes(input.draft.questions[0]));
    assert.equal(page.elements.get('response-score-cards').hidden, true);
    assert.match(read('response-detail.html'), /<button[^>]*disabled[^>]*aria-describedby="response-export-note"/);
    assert.equal(page.elements.get('response-score-status').textContent, '尚未計分');
});
test('missing, unknown and ambiguous task IDs never fall back to another response', async () => {
    for (const search of ['', '?taskId=unknown', '?taskId=response-ui-snap&taskId=other']) {
        const page = await mount([task()], search); assert.equal(page.elements.get('response-record').hidden, true);
    }
    const duplicate = await mount([task(), task()]); assert.equal(duplicate.elements.get('response-record').hidden, true);
});
test('storage refresh clears old answers when a response becomes unavailable', async () => {
    const tasks = [task()]; const page = await mount(tasks); tasks.length = 0; await page.handlers.get('storage')();
    assert.equal(page.elements.get('response-record').hidden, true);
    assert.equal(page.elements.get('response-questions').children.length, 0);
    assert.equal(page.elements.get('response-observation').textContent, '');
});
test('medical list navigation replaces its raw-answer popup and returns to forms tab', () => {
    const list = read('js/system-demo-ui.js');
    assert.match(list, /fixtures\.responseHref\(entry,location.search\)/);
    assert.doesNotMatch(list, /dialog\.showModal|data-demo-result.*onclick/);
    assert.match(read('response-detail.html'), /href="case-detail\.html#shared-tasks"/);
    assert.match(list, /location.hash==='#shared-tasks'/);
    assert.doesNotMatch(read('js/response-detail.js'), /fetch\(|SharedDemo\.(save|assign)|localStorage\.setItem|totalScore\s*=/);
});

const medicalFixtures = medicalClient;
const medicalSearch = '?source=medical-demo&caseId=DEMO-001&taskId=' + medicalFixtures.taskId;
test('explicit medical fixture renders 26 answers, named teacher, unscored result and mode-preserving return', async () => {
    const page = await mount([], medicalSearch);
    assert.equal(page.elements.get('response-record').hidden, false);
    assert.equal(page.elements.get('response-demo-label').hidden, false);
    assert.equal(page.elements.get('response-questions').children.length, 26);
    assert.equal(page.elements.get('response-respondent').textContent, '示範教師');
    assert.equal(page.elements.get('response-score-status').textContent, '尚未計分');
    assert.equal(page.elements.get('response-score-cards').children.length, 0);
    for (const id of ['response-back-top', 'response-back-bottom'])
        assert.equal(page.elements.get(id).href, 'case-detail.html#shared-tasks');
});
test('medical detail rejects disabled mode, missing/unknown/duplicate task, wrong case and ambiguous parameters', async () => {
    for (const search of [medicalSearch + '&medicalDemo=0', medicalSearch.replace('DEMO-001', 'DEMO-024'),
        medicalSearch.replace(medicalFixtures.taskId, 'unknown'), medicalSearch.replace('&taskId=' + medicalFixtures.taskId, ''),
        medicalSearch + '&taskId=other', medicalSearch + '&caseId=DEMO-024', medicalSearch + '&medicalDemo=1&medicalDemo=1',
        medicalSearch.replace('caseId=DEMO-001&', ''), medicalSearch + '&source=api']) {
        const page = await mount([], search);
        assert.equal(page.elements.get('response-record').hidden, true, search);
        assert.equal(page.elements.get('response-demo-label').hidden, true);
        assert.equal(page.elements.get('response-questions').children.length, 0);
    }
});
test('normal stored results remain readable but a supplied wrong case is refused', async () => {
    const page = await mount([task()], '?taskId=response-ui-snap&caseId=DEMO-001&source=local-demo');
    assert.equal(page.elements.get('response-record').hidden, false);
    assert.equal(page.elements.get('response-demo-label').hidden, true);
    assert.equal((await mount([task()], '?taskId=response-ui-snap&caseId=DEMO-024')).elements.get('response-record').hidden, true);
});
for (const status of [401, 403, 503, 'network']) {
    test(`lookup failure ${status} never reveals the synthetic result, even with presentation mode enabled`, async () => {
        const page = await mount([], '?medicalDemo=1&taskId=local-unavailable&caseId=DEMO-001', () => {
            throw Object.assign(new Error('lookup failed'), { status });
        });
        assert.equal(page.elements.get('response-record').hidden, true);
        assert.equal(page.elements.get('response-demo-label').hidden, true);
    });
}
test('an API-designated source cannot select a fixture or use local data as fallback', async () => {
    const page = await mount([task()], medicalSearch.replace('source=medical-demo', 'source=api'));
    assert.equal(page.elements.get('response-record').hidden, true);
    assert.equal(page.elements.get('response-questions').children.length, 0);
});
