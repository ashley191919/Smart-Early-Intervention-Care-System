"use strict";
// Presentation/accessibility only: never saves answers or changes authorization.
(()=>{
 if(!document.body.classList.contains('parent-shell'))return;
 const form=document.getElementById('questionnaire');
 if(form&&document.body.classList.contains('parent-fill')){
  const progress=document.createElement('p');progress.className='parent-mobile-progress';progress.setAttribute('role','status');form.querySelector('#questions').before(progress);
  const update=()=>{const total=form.querySelectorAll('.question').length,filled=form.querySelectorAll('.question input[type=radio]:checked').length;progress.textContent=`已填 ${filled}／${total} 題`;};
  form.addEventListener('change',update);new MutationObserver(update).observe(document.getElementById('questions'),{childList:true});update();
  const save=document.getElementById('save-draft');if(save)save.textContent='先存起來，下次再填';
  const field=document.getElementById('respondent-relationship');if(field){const label=field.closest('label')?.querySelector('.field-label');if(label)label.innerHTML='您與孩子的關係 <span class="required">必填</span>';}
  const hint=form.querySelector('label[for=observation] .hint');if(hint)hint.textContent='（選填，可以不填）';
  form.addEventListener('submit',()=>{const first=form.querySelector('.question.invalid'),person=document.getElementById('respondent-name'),date=document.getElementById('respondent-date');if(first&&person.value.trim()&&date.value&&date.validity.valid){const number=Number(first.id.replace('q-',''))+1;document.getElementById('form-error').textContent=`第 ${number} 題還沒填，請選一個答案。`;}});
 }
})();
