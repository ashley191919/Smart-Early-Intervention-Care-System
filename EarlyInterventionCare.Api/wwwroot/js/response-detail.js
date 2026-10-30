(() => {
    "use strict";
    // UI contract only: a future authorized response adapter may supply confirmed scoring.
    // The fixture adapter below ALWAYS supplies null; browser data is not a score source.
    function buildScoreView(result, completed) {
        const empty = { status: "尚未計分", note: "尚無可顯示的計分結果。", cards: [], meta: [] };
        if (!completed) return { ...empty, status: "未完成", note: "問卷尚未提交，尚無計分結果。" };
        if (result?.status === "NOT_APPLICABLE") return { ...empty, status: "不適用", note: "此問卷不提供計分結果。" };
        if (result?.status !== "SCORED") return empty;
        const cards = [];
        const valid = item => item && typeof item.value === "number" && Number.isFinite(item.value);
        const unit = item => typeof item.unit === "string" ? item.unit : "";
        if (valid(result.total)) cards.push({ label: "總分", value: result.total.value, unit: unit(result.total), total: true });
        if (Array.isArray(result.subscales)) result.subscales.forEach(item => {
            if (valid(item) && typeof item.label === "string" && item.label.trim())
                cards.push({ label: item.label, value: item.value, unit: unit(item), total: false });
        });
        if (!cards.length) return empty;
        const meta = [];
        if (typeof result.method === "string" && result.method.trim()) meta.push(["計分方式", result.method]);
        if (typeof result.version === "string" && result.version.trim()) meta.push(["計分版本", result.version]);
        return { status: "已計分", note: "", cards, meta };
    }
    function formatTime(value) {
        if (value === null || value === undefined || value === "") return "—";
        if (!["string", "number"].includes(typeof value)) return "—";
        const date = new Date(value);
        return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat("zh-TW", {
            timeZone: "Asia/Taipei", year: "numeric", month: "2-digit", day: "2-digit",
            hour: "2-digit", minute: "2-digit", hour12: false
        }).format(date) : "—";
    }
    function buildFixtureView(task) {
        if (!task || typeof task !== "object") return null;
        const draft = task.draft;
        const completed = task.status === "done" && draft && formatTime(draft.submittedAt) !== "—";
        if (!completed) return { completed: false };
        const questions = Array.isArray(draft.questions) ? draft.questions : [];
        const options = Array.isArray(draft.options) ? draft.options : [];
        const answers = draft.answers && typeof draft.answers === "object" ? draft.answers : {};
        const indexes = [...new Set([...questions.map((_, i) => i), ...Object.keys(answers)
            .filter(key => /^(0|[1-9]\d*)$/.test(key)).map(Number)])].sort((a, b) => a - b);
        return {
            completed: true, name: typeof task.name === "string" && task.name.trim() ? task.name : "未提供問卷名稱",
            version: typeof task.version === "string" && task.version.trim() ? task.version : "未提供",
            caseCode: typeof task.caseId === "string" && task.caseId.trim() ? task.caseId : "未提供",
            caseName: task.caseId === "DEMO-001" ? "王○安" : "未提供",
            role: ({ parent: "家長", teacher: "教師" })[task.role] || "未提供",
            submittedAt: formatTime(draft.submittedAt),
            observation: typeof draft.note === "string" && draft.note.trim() ? draft.note : "未填寫補充觀察",
            // Ignore every score-like field in localStorage. No confirmed score source exists.
            scoring: buildScoreView(null, true),
            questions: indexes.map(index => {
                const value = answers[index];
                const label = Number.isInteger(value) && typeof options[value] === "string" ? options[value] : null;
                return { number: index + 1, text: typeof questions[index] === "string" ? questions[index] : "題目內容未提供",
                    answer: label ?? (value === undefined ? "未提供答案" : Number.isInteger(value) ?
                        "選項代碼 " + value + "（答案標籤未提供）" : "答案格式無法辨識") };
            })
        };
    }
    if (typeof module !== "undefined" && module.exports) module.exports = { buildFixtureView, buildScoreView, formatTime };
    if (typeof document === "undefined") return;
    const $ = id => document.getElementById(id);
    const state = $("response-state"), record = $("response-record");
    if (!state || !record) return;
    const text = (id, value) => { $(id).textContent = value; };
    function node(tag, className, content) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (content !== undefined) element.textContent = content;
        return element;
    }
    function showState(message) {
        record.hidden = true; state.hidden = false; state.textContent = message;
        $("response-demo-label").hidden = true;
        $("response-respondent-row").hidden = true;
        // A deleted or no-longer-submitted fixture must not leave old answers in the page.
        ["response-name", "response-version", "response-case-code", "response-case-name", "response-role",
            "response-submitted", "response-status", "response-score-status", "response-score-note",
            "response-question-count", "response-observation", "response-score-cards", "response-score-meta",
            "response-questions", "response-respondent"].forEach(id => $(id).replaceChildren());
        document.title = "問卷填答詳細｜早療智慧動態照護系統";
    }
    let revision = 0;
    async function render() {
        const current = ++revision;
        try {
            const params = new URLSearchParams(location.search);
            const caseIds = params.getAll("caseId");
            // Formal result/exports have no approved Response API adapter yet.
            // Never call SharedDemo or the protected Development fixture to fill an API result.
            const apiMode = window.MedicalApiConfig?.caseSource === "api" ||
                params.getAll("source").includes("api") ||
                caseIds.some(id => window.MedicalApi?.uuid(id));
            if (apiMode) {
                const caseId = caseIds.length === 1 && window.MedicalApi?.uuid(caseIds[0]) ? caseIds[0] : null;
                const href = caseId ? "case-detail.html?source=api&caseId=" + encodeURIComponent(caseId) + "#shared-tasks" : "cases.html?source=api";
                $("response-back-top").href = href;
                $("response-back-bottom").href = href;
                return showState("尚未提供正式結果");
            }
            const fixtures = window.MedicalDemo;
            const returnHref = fixtures.caseHref(caseIds.length === 1 ? caseIds[0] : "DEMO-001", location.search);
            $("response-back-top").href = returnHref;
            $("response-back-bottom").href = returnHref;
            const ids = params.getAll("taskId");
            if (ids.length !== 1 || !ids[0]) return showState("請從個案的表單紀錄選擇一份已提交問卷。");
            if (params.get("source") === "medical-demo" && !window.StaffAuth?.getCurrentUser())
                return showState("請登入醫療端後查看展示紀錄。");
            const localTasks = params.get("source") === "medical-demo" ? [] : window.SharedDemo?.tasks() || [];
            const entry = await fixtures.resolve(localTasks, location.search);
            if (current !== revision) return;
            if (!entry) return showState("找不到這份填答紀錄，請返回表單紀錄重新選擇。");
            const task = entry.task;
            $("response-back-top").href = fixtures.caseHref(task.caseId, location.search);
            $("response-back-bottom").href = fixtures.caseHref(task.caseId, location.search);
            const view = buildFixtureView(task);
            if (!view) return showState("找不到這份填答紀錄，請返回表單紀錄重新選擇。");
            if (!view.completed) return showState("問卷尚未提交，原始填答紀錄於提交後提供。");
            $("response-demo-label").hidden = entry.source !== "medical-demo";
            const respondent = typeof task.draft.respondent === "string" ? task.draft.respondent.trim() : "";
            text("response-respondent", respondent);
            $("response-respondent-row").hidden = !respondent;
            text("response-name", view.name); text("response-version", view.version);
            text("response-case-code", view.caseCode); text("response-case-name", view.caseName);
            text("response-role", view.role); text("response-submitted", view.submittedAt);
            text("response-status", "已提交"); text("response-score-status", view.scoring.status);
            text("response-score-note", view.scoring.note);
            $("response-score-note").hidden = !view.scoring.note;
            const cards = $("response-score-cards");
            cards.replaceChildren(...view.scoring.cards.map(card => {
                const tile = node("div", "response-score-tile" + (card.total ? " response-score-total" : ""));
                tile.append(node("span", "response-score-label", card.label));
                const value = node("p", "response-score-value");
                value.append(node("strong", "", String(card.value)), node("span", "", card.unit));
                tile.append(value); return tile;
            }));
            cards.hidden = !view.scoring.cards.length;
            const meta = $("response-score-meta");
            meta.replaceChildren(...view.scoring.meta.map(([label, value]) => {
                const row = node("div"); row.append(node("dt", "", label), node("dd", "", value)); return row;
            }));
            meta.hidden = !view.scoring.meta.length;
            text("response-question-count", view.questions.length + " 題");
            const list = $("response-questions");
            list.replaceChildren(...view.questions.map(question => {
                const item = node("li", "response-question"), heading = node("h3", "response-question-heading");
                heading.append(node("span", "response-question-number", String(question.number)), node("span", "", "第 " + question.number + " 題"));
                const answer = node("dl", "response-answer");
                answer.append(node("dt", "", "原始答案"), node("dd", "", question.answer));
                item.append(heading, node("p", "response-question-text", question.text), answer); return item;
            }));
            if (!view.questions.length) list.append(node("li", "response-question", "此紀錄未提供題目與答案。"));
            text("response-observation", view.observation);
            document.title = view.name + "・填答詳細｜早療智慧動態照護系統";
            state.hidden = true; record.hidden = false;
        } catch { if (current === revision) showState("暫時無法讀取填答紀錄，請返回表單紀錄重新選擇。"); }
    }
    render();
    window.addEventListener("pageshow", render);
    window.addEventListener("storage", render);
    document.addEventListener("staff-authenticated", render);
    document.addEventListener("medical-demo-invalidated", () => {
        if (new URLSearchParams(location.search).get("source") === "medical-demo") {
            revision++;
            showState("請重新驗證醫療端登入狀態。");
        }
    });
})();
