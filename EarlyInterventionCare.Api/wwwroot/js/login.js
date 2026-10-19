"use strict";

// UI prototype only. Replace these submit handlers with A/C's agreed APIs.
// Do not store or log passwords, phone numbers, identifiers or authorization codes.
const tabs = Array.from(document.querySelectorAll(".role-tab"));
const panels = Array.from(document.querySelectorAll(".role-panel"));
const dialog = document.querySelector("#info-dialog");

function clearLegacyMedicalPassword() {
  const password = document.querySelector("#medical-form #staff-password");
  if (password) password.value = "";
}

function redirectToStaffLogin() {
  clearLegacyMedicalPassword();
  window.location.assign("/staff-login.html");
}

// Also clear restored form values when returning through the browser's page cache.
clearLegacyMedicalPassword();
window.addEventListener("pagehide", clearLegacyMedicalPassword);
window.addEventListener("pageshow", clearLegacyMedicalPassword);

function selectRole(tab, focus = false) {
  // The medical workspace has a dedicated authenticated login page.  Keep the
  // shared page for the parent and teacher flows only, so every medical entry
  // uses the staff session API instead of the legacy prototype form below.
  if (tab.id === "tab-medical") {
    redirectToStaffLogin();
    return;
  }

  tabs.forEach(item => {
    const selected = item === tab;
    item.setAttribute("aria-selected", String(selected));
    item.tabIndex = selected ? 0 : -1;
  });
  panels.forEach(panel => {
    panel.hidden = panel.id !== tab.getAttribute("aria-controls");
    panel.querySelectorAll(".form-status").forEach(status => { status.hidden = true; status.textContent = ""; });
    // Switching roles must not leave credentials in an inactive form.
    const form = panel.querySelector("form");
    form.reset();
    panel.querySelectorAll("[aria-invalid]").forEach(input => input.removeAttribute("aria-invalid"));
  });
  document.querySelector("#phone-error").hidden = true;
  document.querySelector("#faq-section").hidden = tab.id === "tab-teacher";
  document.dispatchEvent(new CustomEvent("login-role-change", { detail: tab.id }));
  if (focus) tab.focus();
}

tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => selectRole(tab));
  tab.addEventListener("keydown", event => {
    let next;
    if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
    if (event.key === "ArrowLeft") next = (index + tabs.length - 1) % tabs.length;
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = tabs.length - 1;
    if (next !== undefined) { event.preventDefault(); selectRole(tabs[next], true); }
  });
});

function showStatus(form, message) {
  const status = form.querySelector(".form-status");
  status.textContent = message;
  status.hidden = false;
}

const legacyMedicalForm = document.querySelector("#medical-form");
legacyMedicalForm.addEventListener("submit", event => {
  event.preventDefault();
  redirectToStaffLogin();
});
legacyMedicalForm.querySelector('a[href="/staff-login.html"]')?.addEventListener("click", clearLegacyMedicalPassword);

function openInfo(title, paragraphs) {
  document.querySelector("#dialog-title").textContent = title;
  const content = document.querySelector("#dialog-content");
  content.replaceChildren(...paragraphs.map(text => {
    const p = document.createElement("p");
    p.textContent = text;
    return p;
  }));
  dialog.showModal();
}

document.querySelector("#open-faq").addEventListener("click", () => openInfo("系統操作說明與常見問題", [
  "家長／照顧者：使用手機號碼與 OTP 登入；第一次使用時，再以醫護提供的一次性邀請碼綁定孩子。",
  "幼兒園教師：使用家長提供的授權碼或 QR Code，進入指定問卷。",
  "醫療行政：使用院方核准的單位代碼、帳號與密碼登入，不開放自行註冊。",
  "請依您的身分選擇登入方式。"
]));
document.querySelectorAll("[data-info]").forEach(button => button.addEventListener("click", () => openInfo(
  button.textContent, ["此處預留正式說明內容。隱私權政策與資料保護安全聲明須由專題團隊與院方確認後提供。", "本試做版不傳送或保存登入欄位資料；請使用虛構資料測試畫面。"]
)));
document.querySelector("#close-dialog").addEventListener("click", () => dialog.close());
document.querySelector("#dialog-done").addEventListener("click", () => dialog.close());

// Optional direct links for reviewing each role without altering the server APIs.
const requestedRole = new URLSearchParams(location.search).get("role");
if (["parent", "teacher", "medical"].includes(requestedRole)) {
  selectRole(document.querySelector(`#tab-${requestedRole}`));
}
