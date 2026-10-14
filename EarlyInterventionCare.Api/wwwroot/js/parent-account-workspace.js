"use strict";
(() => {
 const session = window.EarlyCareParentSession;
 if (new URLSearchParams(location.search).get("mode") !== "account" && !session.token()) return;
 const api = EarlyCareParentClient.createClient(session, window.fetch.bind(window));
 const renderGate = EarlyCareParentClient.latestRequest(), loadGate = EarlyCareParentClient.latestRequest();
 const root = document.querySelector("#page-content"), select = document.querySelector("#child-select"), modal = document.querySelector("#modal");
 const count = document.querySelector(".nav-count"), bind = document.querySelector('[data-action="bind"]');
 let children = [], profiles = [], currentId = "", page = "home", loaded = false;
 const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
 const statusNames = {NEW:"新個案",TO_CONTACT:"待聯絡",APPOINTED:"已預約",WAITING_FORM:"待填表",FORM_COMPLETED:"表單完成",COMPLETED:"已完成"};
 const relations = {FATHER:"父親",MOTHER:"母親",GRANDPARENT:"祖父母",RELATIVE:"其他親屬",GUARDIAN:"監護人",CAREGIVER:"照顧者",OTHER:"其他"};
 const taskNames = {PENDING:"待填寫",IN_PROGRESS:"填寫中",SUBMITTED:"已提交"};
 const labels = {home:["家長照顧儀表板","查看孩子資料與本次評估準備。"],children:["我的孩子","新增孩子時使用醫護提供的邀請碼。"],tasks:["我的問卷","查看醫護指定的家長問卷。"],teacher:["教師問卷完成進度","家長僅查看教師是否完成問卷。"],help:["使用協助","需要更正孩子資料時，請聯絡醫護人員。"],consent:["同意與資料使用","如需查閱同意紀錄，請聯絡承辦窗口。"]};
 function errorPanel(error) {
  return `<section class="panel account-card" role="alert"><p>${esc(error.message)}</p><a class="btn secondary" href="/?role=parent&mode=account">返回家長登入</a><button class="btn secondary" data-action="reload-account">重新載入</button></section>`;
 }
 function clearData() {
  loaded = false; children = []; profiles = []; select.replaceChildren(); select.disabled = true; count.textContent = "—"; bind.disabled = true;
 }
 function dialog(title, html) {
  document.querySelector("#modal-title").textContent = title;
  document.querySelector("#modal-body").innerHTML = html;
  modal.showModal();
 }
 function childCard(child) {
  return `<section class="panel account-card"><div class="account-card-heading"><h2>${esc(child.childName)}</h2><span class="tag">${esc(statusNames[child.caseStatus] || child.caseStatus)}</span></div><p>個案代碼 ${esc(child.caseCode)}</p><p>身分證字號 ${esc(child.nationalId)}</p><p>地址 ${esc(child.address)}</p><p>出生日期 ${esc(child.birthDate)} · ${esc({MALE:"男",FEMALE:"女",UNKNOWN:"未提供"}[child.sex])}</p><p>與孩子的關係：${child.relationships.map(r => esc(relations[r.relationship] || r.relationship) + (r.isPrimaryContact ? "（主要聯絡人）" : "")).join("、") || "未提供"}</p></section>`;
 }
 function profileCards(child) {
  const ids = child?.relationships.map(r => r.guardianId);
  const rows = ids ? profiles.filter(p => ids.includes(p.guardianId)) : profiles;
  return `<section class="panel account-card"><div class="account-card-heading"><h2>我的聯絡資料</h2><button class="text-button" data-action="reload-account">重新載入資料</button></div>${rows.length ? rows.map(p => `<p>${esc(p.displayName)} · ${esc(p.phone)} · ${esc(p.email)} <button class="btn secondary" data-edit-profile="${esc(p.guardianId)}">修改姓名</button></p>`).join("") : "<p>完成孩子綁定後可查看聯絡資料。</p>"}</section>`;
 }
 function tasksPanel(tasks, compact = false) {
  const rows = compact ? tasks.filter(t => ["PENDING","IN_PROGRESS"].includes(t.taskStatus)).slice(0,3) : tasks;
  return `<section class="panel account-card"><div class="account-card-heading"><h2>${compact ? "待填問卷" : "家長問卷"}</h2>${compact ? '<button class="text-button" data-page="tasks">查看全部 →</button>' : ""}</div>${rows.length ? rows.map(t => `<div class="account-task-row"><div><strong>${esc(t.questionnaireTitle)}</strong><p>第 ${esc(t.assignmentRound)} 輪 · 版本 ${esc(t.versionNumber)} · ${t.isRequired ? "必填" : "選填"}</p></div><span class="tag">${esc(taskNames[t.taskStatus] || t.taskStatus)}</span></div>`).join("") : "<p>目前沒有待填問卷。</p>"}</section>`;
 }
 async function render() {
  const request = renderGate.begin(), selectedId = currentId, selectedPage = page;
  document.querySelector("#page-title").textContent = labels[page][0];
  document.querySelector("#page-subtitle").textContent = labels[page][1];
  root.innerHTML = '<p role="status">正在載入…</p>'; count.textContent = "—";
  try {
   if (!loaded || !session.token()) throw new Error("請先登入家長帳號。");
   // Rendering all children must refresh ALL scopes, not just recheck the selected child.
   if(selectedPage==="children"){
    const freshChildren=await EarlyCareParentClient.loadChildren(api),freshProfiles=await api("/api/parent/profile");
    if(!renderGate.current(request))return;
    children=freshChildren;profiles=freshProfiles;
    if(!children.some(c=>c.caseId===currentId))currentId=children[0]?.caseId||"";
    select.innerHTML=children.map(c=>`<option value="${esc(c.caseId)}">${esc(c.childName)} · ${esc(c.caseCode)}</option>`).join("");
    select.value=currentId;select.disabled=!children.length;
   }
   const child = children.find(c => c.caseId === (selectedPage==="children"?currentId:selectedId));
   // Recheck Case scope on every page switch. Do not render previously cached clinical data after revocation.
   const tasks = child ? await api(`/api/parent/cases/${child.caseId}/tasks`) : [];
   let html;
   if (selectedPage === "home") {
    const summary = EarlyCareParentClient.summarize(tasks);
    html = child ? childCard(child) : '<section class="panel account-card"><h2>尚未綁定孩子</h2><p>請使用醫護提供的邀請碼新增孩子。</p></section>';
    html += `<div class="account-summary"><section class="panel account-card"><strong>${summary.pending}</strong><p>份待填寫</p></section><section class="panel account-card"><strong>${summary.drafts}</strong><p>份填寫中</p></section><section class="panel account-card"><strong>${summary.submitted}</strong><p>份已提交</p></section></div>`;
    html += '<p class="privacy-note">統計目前孩子所有已指派輪次的家長問卷。</p>' + tasksPanel(tasks,true) + profileCards(child);
   } else if (selectedPage === "children") {
    html = (children.length ? children.map(childCard).join("") : '<section class="panel account-card"><p>尚未綁定孩子，請使用醫護邀請碼新增。</p></section>') + profileCards(child);
   } else if (selectedPage === "tasks") html = child ? tasksPanel(tasks) : '<p>請先綁定孩子。</p>';
   else if (selectedPage === "teacher") {
    const rows = child ? await api(`/api/parent/cases/${child.caseId}/teacher-completion`) : [];
    html = `<section class="panel account-card"><h2>教師問卷完成進度</h2>${rows.length ? rows.map(t => `<p><strong>${esc(t.questionnaireTitle)}</strong> · 第 ${esc(t.assignmentRound)} 輪 · ${t.isCompleted ? "已完成" : "尚未完成"}</p>`).join("") : "<p>目前沒有指定教師問卷。</p>"}</section>`;
   } else html = `<section class="panel account-card"><p>${esc(labels[selectedPage][1])}</p></section>`;
   if (renderGate.current(request)) { root.innerHTML = html; count.textContent = String(EarlyCareParentClient.summarize(tasks).outstanding); }
  } catch (error) {
   if (renderGate.current(request)) {
    if ([401,403,404].includes(error.status)) clearData();
    root.innerHTML = errorPanel(error); count.textContent = "—";
   }
  }
 }
 async function load() {
  const request = loadGate.begin(); renderGate.begin(); clearData(); root.innerHTML = '<p role="status">正在載入…</p>';
  try {
   const list = await EarlyCareParentClient.loadChildren(api);
   if (!loadGate.current(request)) return;
   const ownProfiles = await api("/api/parent/profile");
   if (!loadGate.current(request)) return;
   children = list; profiles = ownProfiles; loaded = true;
   if (!children.some(c => c.caseId === currentId)) currentId = children[0]?.caseId || "";
   select.innerHTML = children.map(c => `<option value="${esc(c.caseId)}">${esc(c.childName)} · ${esc(c.caseCode)}</option>`).join("");
   select.value = currentId; select.disabled = !children.length; bind.disabled = false;
   await render();
  } catch (error) { if (loadGate.current(request)) { clearData(); root.innerHTML = errorPanel(error); } }
 }
 select.addEventListener("change", () => { currentId = select.value; render(); });
 document.querySelector("#close-modal").addEventListener("click", () => modal.close());
 document.addEventListener("click", event => {
  const nav = event.target.closest("[data-page]");
  if (nav) {
   page = nav.dataset.page;
   document.querySelectorAll(".nav [data-page]").forEach(button => { button.classList.toggle("active",button.dataset.page === page); button.removeAttribute("aria-current"); if (button.dataset.page === page) button.setAttribute("aria-current","page"); });
   const crumb = document.querySelector("#crumb"); if (crumb) crumb.textContent = labels[page][0];
   document.querySelector("#content").scrollTop = 0; render();
  }
  if (event.target.closest('[data-action="reload-account"]')) { if (modal.open) modal.close(); load(); }
  if (event.target.closest('[data-action="bind"]') && loaded) dialog("綁定孩子",'<form id="account-bind"><label for="binding-code">醫護提供的邀請碼</label><input class="account-input" id="binding-code" name="invitationCode" maxlength="80" required autocomplete="off"><p class="account-error" role="alert" hidden></p><button class="btn" type="submit">確認綁定</button></form>');
  const edit = event.target.closest("[data-edit-profile]");
  if (edit && loaded) {
   const profile = profiles.find(p => p.guardianId === edit.dataset.editProfile); if (!profile) return;
   dialog("修改聯絡人姓名",`<form id="account-profile" data-guardian-id="${esc(profile.guardianId)}"><label for="profile-name">姓名</label><input class="account-input" id="profile-name" name="displayName" value="${esc(profile.displayName)}" maxlength="100" required><p>Email：${esc(profile.email)}</p><p>手機：${esc(profile.phone)}。如需變更手機，請聯絡醫護並重新驗證。</p><p class="account-error" role="alert" hidden></p><button class="btn" type="submit">儲存</button><button class="btn secondary" type="button" data-action="reload-account" hidden>重新載入最新資料</button></form>`);
  }
 });
 document.addEventListener("submit", async event => {
  if (!["account-bind","account-profile"].includes(event.target.id)) return;
  event.preventDefault();
  const form = event.target, button = form.querySelector('button[type="submit"]'), error = form.querySelector(".account-error");
  button.disabled = true; error.hidden = true;
  let conflict = false;
  try {
   if (form.id === "account-bind") await api("/api/parent/bindings",{method:"POST",body:JSON.stringify({invitationCode:form.elements.invitationCode.value.trim()})});
   else {
    const profile = profiles.find(p => p.guardianId === form.dataset.guardianId);
    await api("/api/parent/profile/" + profile.guardianId,{method:"PUT",body:JSON.stringify({displayName:form.elements.displayName.value.trim(),revision:profile.revision})});
   }
   modal.close(); await load();
  } catch (reason) {
   error.textContent = reason.message; error.hidden = false;
   if (reason.status === 409 && form.id === "account-profile") { conflict = true; form.querySelector('[data-action="reload-account"]').hidden = false; }
   if (reason.status === 401) { modal.close(); clearData(); root.innerHTML = errorPanel(reason); }
  } finally { button.disabled = conflict; }
 });
 load();
})();
