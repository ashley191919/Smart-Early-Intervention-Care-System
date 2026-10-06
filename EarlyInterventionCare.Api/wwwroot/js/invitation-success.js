(() => {
    "use strict";

    // 固定專題展示範例：不接收上一頁資料，不建立指派、有效 token 或 SMS。
    // 未來正式流程：醫療人員選表單與角色 → 確認 → 後端建立 Form Assignment
    // → 後端產生家長 invitation token 與 URL → 醫療人員透過 SMS 傳給家長
    // → 家長點擊 URL 進入自己的填答流程。
    // 正式 invitation token 由後端產生，必須不可猜測、不暴露 caseId / parentId / childId，
    // 並具有期限、有效性檢查及可失效狀態；前端不可自行產生正式 token。
    // 若有教師任務：家長端顯示教師協作填寫 → 家長確認授權 → 後端產生教師授權碼
    // → 家長自行提供給教師 → 教師輸入並驗證 → 只能看到原先指派給教師的表單。
    // 家長專屬 invitation 連結與教師授權碼是不同的授權資訊；醫療人員指定教師任務
    // 不等於教師已取得權限。本醫療端 Demo 不產生或顯示教師授權碼。
    // 未來後端簡訊概念（另附家長專屬 URL，不在前端存放完整手機）：
    // 「輔仁大學附設醫院早療填答通知：您有待完成的早療相關表單，
    // 請於期限內透過專屬連結進入填寫。請勿將此連結提供給他人。」

    const page = document.querySelector(".invitation-content");
    if (!page) return;

    const url = page.querySelector("#invitation-url");
    const copyButton = page.querySelector("#invitation-copy");
    const copyFeedback = page.querySelector("#invitation-copy-feedback");
    const smsButton = page.querySelector("#invitation-send-sms");
    const smsFeedback = page.querySelector("#invitation-sms-feedback");

    function showCopyFeedback(message, isError) {
        copyFeedback.textContent = message;
        copyFeedback.classList.toggle("is-error", isError);
        copyFeedback.hidden = false;
    }

    copyButton.addEventListener("click", async () => {
        copyButton.disabled = true;
        copyFeedback.hidden = true;
        try {
            if (typeof navigator === "undefined" || typeof navigator.clipboard?.writeText !== "function") {
                throw new Error("Clipboard API unavailable");
            }
            await navigator.clipboard.writeText(url.textContent.trim());
            showCopyFeedback("已複製連結", false);
        } catch {
            showCopyFeedback("無法自動複製，請手動複製連結", true);
            url.focus();
        } finally {
            copyButton.disabled = false;
        }
    });

    smsButton.addEventListener("click", () => {
        smsFeedback.textContent = "Demo：尚未串接簡訊服務，目前不會實際發送。";
        smsFeedback.hidden = false;
    });
})();
