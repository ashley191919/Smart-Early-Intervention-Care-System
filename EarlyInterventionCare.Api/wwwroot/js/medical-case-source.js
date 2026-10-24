(() => {
    "use strict";
    window.MedicalCaseMode = window.MedicalCases?.route(location.search, window.MedicalApiConfig) || Object.freeze({ mode: "api", invalid: true });
    if (window.MedicalCaseMode.mode !== "api") return;
    document.documentElement.dataset.caseSource = "api";
    // Runs before the legacy Demo scripts and before StaffAuth can expose its protected area.
    const main = document.querySelector(".case-detail-content");
    if (main) main.replaceChildren();
    document.querySelectorAll("dialog").forEach(dialog => dialog.remove());
    document.querySelectorAll('a[href="cases.html"]').forEach(link => { link.href = "cases.html?source=api"; });
    // No confirmed Case write/assignment contract: never navigate from a real record into a Demo write flow.
    document.querySelectorAll('a[href="case-create.html"]').forEach(link => link.remove());
})();
