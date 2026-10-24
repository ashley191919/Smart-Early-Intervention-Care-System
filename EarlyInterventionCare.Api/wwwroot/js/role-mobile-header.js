"use strict";
// Responsive presentation only. Original navigation/logout handlers are reused.
(()=>{
 const sidebar=document.querySelector('.sidebar'),brand=sidebar?.querySelector('.brand'),topbar=document.querySelector('.topbar');
 const sourceHospital=topbar?.querySelector('.hospital-brand,.hospital-header');
 if(!sidebar||!brand||!sourceHospital)return;
 const medical=topbar.hasAttribute('data-staff-header'),parent=document.body.classList.contains('parent-shell');
 const role=medical?'medical':parent?'parent':'teacher';
 const navigation=sidebar.querySelector('.sidebar-menu')||sidebar.querySelector('.main-nav')||sidebar.querySelector('.nav')?.parentElement;
 if(!navigation)return;
 document.body.classList.add('has-role-mobile-header','role-'+role);
 const anchor=node=>{const marker=document.createComment('desktop placement');node.before(marker);return marker;};
 const brandAnchor=anchor(brand),navAnchor=anchor(navigation),actions=medical?topbar.querySelector('.header-actions'):null,actionsAnchor=actions?anchor(actions):null;
 const drawer=document.createElement('div');drawer.id='role-mobile-drawer';drawer.className='role-mobile-drawer';sidebar.append(drawer);
 const header=document.createElement('header');header.className='role-mobile-header';header.setAttribute('aria-label','工作台導覽');
 const hospital=document.createElement('div');hospital.className='role-mobile-hospital';const logo=sourceHospital.querySelector('img').cloneNode(true);logo.alt='輔仁大學附設醫院院徽';
 const copy=document.createElement('div');copy.className='role-mobile-hospital-copy';
 copy.innerHTML='<p class="role-mobile-hospital-name"><span>天主教輔仁大學</span><span>附設醫院</span></p><p class="role-mobile-hospital-english" lang="en">FU JEN CATHOLIC UNIVERSITY HOSPITAL</p>';hospital.append(logo,copy);
 const toggle=document.createElement('button');toggle.className='role-mobile-toggle';toggle.type='button';toggle.textContent='☰';toggle.setAttribute('aria-controls',drawer.id);toggle.setAttribute('aria-expanded','false');toggle.setAttribute('aria-label','開啟選單');header.append(hospital,toggle);sidebar.before(header);
 const identity=document.createElement('div');identity.className='role-mobile-identity';
 const user=document.createElement('p');user.textContent=medical?'醫療人員／個案管理師':parent?'家長／照顧者':'幼兒園教師';identity.append(user);
 const originalLogout=document.querySelector(medical?'#staff-header-logout':'.workspace-logout');
 if(originalLogout){const logout=document.createElement('button');logout.type='button';logout.className='role-mobile-logout';logout.textContent='登出';logout.addEventListener('click',()=>originalLogout.click());identity.append(logout);new MutationObserver(()=>logout.disabled=originalLogout.disabled).observe(originalLogout,{attributes:true,attributeFilter:['disabled']});}
 const feedback=document.querySelector('#staff-header-logout-feedback');if(feedback){const message=document.createElement('p');message.className='role-mobile-feedback';message.setAttribute('role','status');message.hidden=true;identity.append(message);new MutationObserver(()=>{message.textContent=feedback.textContent;message.hidden=feedback.hidden;}).observe(feedback,{attributes:true,childList:true,subtree:true,characterData:true});}
 const open=value=>{sidebar.classList.toggle('role-menu-open',value);sidebar.classList.toggle('parent-menu-open',value);toggle.setAttribute('aria-expanded',String(value));toggle.setAttribute('aria-label',value?'關閉選單':'開啟選單');toggle.textContent=value?'×':'☰';};
 const media=window.matchMedia('(max-width:800px)');
 const layout=()=>{open(false);if(media.matches){drawer.append(brand,navigation,identity);if(actions)header.insertBefore(actions,toggle);}else{brandAnchor.after(brand);navAnchor.after(navigation);if(actions)actionsAnchor.after(actions);}};layout();media.addEventListener('change',layout);
 if(actions)actions.addEventListener('click',e=>{if(e.target.closest('#staff-notification-toggle'))open(false);});
 toggle.addEventListener('click',()=>open(toggle.getAttribute('aria-expanded')!=='true'));
 navigation.addEventListener('click',e=>{if(e.target.closest('a,button'))open(false);});
 document.addEventListener('click',e=>{if(!header.contains(e.target)&&!sidebar.contains(e.target))open(false);});
 document.addEventListener('keydown',e=>{if(e.key==='Escape'&&toggle.getAttribute('aria-expanded')==='true'){open(false);toggle.focus();}});
})();
