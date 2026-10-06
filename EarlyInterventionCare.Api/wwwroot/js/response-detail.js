(() => {
    "use strict";

    // 靜態介面展示：題目、答案、12 分總分、分項及單題得分皆直接寫在 HTML。
    // 本頁沒有互動，不讀取、不送出、不儲存資料，也不實作 SNAP 計分公式或醫療判讀。
    // 未來資料模型：Case → FormAssignment → FormResponse → ScoreResult。
    // FormResponse 至少辨識 responseId、caseId、assignmentId、formId、respondentRole、
    // status、completedAt、answers；ScoreResult 可包含 totalScore、sectionScores、
    // calculatedAt、scoringRuleVersion。此處只是註解，不建立資料庫 Model 或 Migration。
    // 正式流程：送出電子表單 → 後端依 Form scoring rule 計分 → 儲存 Response
    // 與 ScoreResult → 醫療人員查看；正式分數由後端提供，前端不自行推測規則或 cutoff。
    // SNAP 同時指派給家長與教師時：
    // SNAP / parent / Response A 與 SNAP / teacher / Response B 是兩筆獨立紀錄。
    // 每筆紀錄保存各自的答案與 ScoreResult，不能合併兩人答案或直接加總兩人分數。
    // 並非所有表單有計分規則：未來依 hasScoring 決定是否顯示獨立計分 Card，
    // 無計分的一般資料表單只顯示填答內容，不要求 totalScore 或每題得分一定存在。
    // 本 SNAP Demo 的 hasScoring 概念為 true；不是正式資料欄位，也不做任何計算。
    // 目前只展示 SNAP / parent 的單筆已完成 Demo，不實作資料庫或後端。
})();
