'use strict';
(() => {
  const header = document.querySelector('.topbar');
  if (!header || document.querySelector('[data-staff-header]')) return;
  const parent = document.body.classList.contains('parent-workspace') || location.pathname.includes('parent-questionnaire');
  const role = parent ? 'parent' : 'teacher';
  const name = parent ? '家長／照顧者' : '幼兒園教師';
  const subtitle = parent ? '家長照顧工作台' : '教師填答工作台';
  const prior = header.querySelector('.identity,.session');
  const actions = document.createElement('div');
  actions.className = 'workspace-user-actions';
  actions.innerHTML = `<button type="button" class="workspace-user-toggle" aria-label="開啟使用者選單：${name}" aria-expanded="false" aria-controls="workspace-user-popup"><span class="workspace-user-avatar"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21v-2a8 8 0 0 1 16 0v2"/></svg></span><span class="workspace-user-copy"><strong>${name}</strong><small>${subtitle}</small></span><svg class="workspace-user-chevron" viewBox="0 0 24 24" aria-hidden="true"><path d="m7 10 5 5 5-5"/></svg></button><div id="workspace-user-popup" class="workspace-user-popup" hidden><button class="workspace-logout" type="button"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 4H4v16h5M9 12h12m-5-5 5 5-5 5"/></svg>登出</button></div>`;
  // Keep status nodes used by questionnaire rendering, but remove them from presentation.
  if (prior) prior.hidden = true;
  header.append(actions);
  const toggle = actions.querySelector('.workspace-user-toggle');
  const popup = actions.querySelector('.workspace-user-popup');
  const logout = actions.querySelector('.workspace-logout');
  function close() { popup.hidden = true; toggle.setAttribute('aria-expanded','false'); }
  toggle.addEventListener('click',() => { popup.hidden = !popup.hidden; toggle.setAttribute('aria-expanded',String(!popup.hidden)); });
  toggle.addEventListener('keydown',event => { if(event.key === 'ArrowDown') { event.preventDefault(); popup.hidden=false; toggle.setAttribute('aria-expanded','true'); logout.focus(); } });
  document.addEventListener('click',event => { if(!actions.contains(event.target)) close(); });
  document.addEventListener('focusin',event => { if(!actions.contains(event.target)) close(); });
  document.addEventListener('keydown',event => { if(event.key === 'Escape' && !popup.hidden) { close(); toggle.focus(); } });
  logout.addEventListener('click',async () => {
    logout.disabled=true;
    try {
      if (typeof authorizedMode !== 'undefined' && authorizedMode && typeof leaveAuthorizedWorkspace === 'function') {
        await leaveAuthorizedWorkspace();
        return;
      }
      if (typeof current !== 'undefined' && current && typeof saveDraft === 'function' && !await saveDraft(true)) return;
      const accountMode=parent&&(new URLSearchParams(location.search).get('mode')==='account'||!!sessionStorage.getItem("earlycare.parent.accessToken"));
      if (parent) sessionStorage.removeItem("earlycare.parent.accessToken");
      if (!parent) sessionStorage.removeItem('earlycare.demo.teacher');
      location.assign('/?role=' + role+(accountMode?'&mode=account':''));
    } finally { logout.disabled=false; }
  });
})();
