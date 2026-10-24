"use strict";
(function(root){
 const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
 const revision=value=>typeof value==='string'&&/^(0|[1-9][0-9]*)$/.test(value)&&BigInt(value)<=18446744073709551615n;
 function create(api,uuid){
  const states=new Map();
  function state(id){if(!guid.test(id))throw new Error('問卷任務識別有誤。');if(!states.has(id))states.set(id,{revision:null,busy:false,conflict:false,pending:null});return states.get(id);}
  async function draft(taskId){
   const s=state(taskId);if(s.busy)throw new Error('請等待目前操作完成。');s.busy=true;
   try{const value=await api(`/api/parent/tasks/${taskId}/draft`);
    if(value?.taskId!==taskId||!guid.test(value.questionnaireVersionId)||!revision(value.revision)||!Array.isArray(value.answers)||
      !['PENDING','IN_PROGRESS'].includes(value.taskStatus))throw new Error('無法確認草稿版本，請重新載入。');
    s.revision=value.revision;s.conflict=false;return value;
   }catch(e){s.revision=null;throw e;}finally{s.busy=false;}
  }
  async function write(taskId,input,submit){
   const s=state(taskId);
   if(s.busy)throw new Error('正在儲存，請勿重複送出。');
   if(s.conflict||s.revision===null)throw new Error('請先重新載入草稿再儲存。');
   if(!submit&&s.pending)throw new Error('上次提交結果尚未確認，請先查詢回執或重試原提交。');
   const payload={...input,expectedRevision:s.revision};
   if(!guid.test(payload.questionnaireVersionId))throw new Error('問卷版本無效。');
   if(submit){
    const fingerprint=JSON.stringify(payload);
    if(s.pending&&s.pending.fingerprint!==fingerprint)throw new Error('上次提交結果尚未確認，請保留原內容並重試，或重新查詢回執。');
    if(!s.pending)s.pending={fingerprint,key:uuid()};
   }
   s.busy=true;
   try{
    const result=await api(`/api/parent/tasks/${taskId}/${submit?'submit':'draft'}`,{
     method:submit?'POST':'PUT',headers:submit?{'Idempotency-Key':s.pending.key}:{},body:JSON.stringify(payload)});
    if(result?.taskId!==taskId||result.questionnaireVersionId!==input.questionnaireVersionId)throw new Error('伺服器回應與目前問卷不符，請確認保存結果。');
    if(submit){if(!guid.test(result.responseId)||result.taskStatus!=='SUBMITTED'||!Number.isFinite(Date.parse(result.submittedAtUtc)))throw new Error('無法確認提交回執，請重試原提交或查詢回執。');s.pending=null;s.revision=null;}
    else{if(!revision(result.revision)||BigInt(result.revision)<=BigInt(s.revision)||result.taskStatus!=='IN_PROGRESS')throw new Error('無法確認草稿保存結果，請重新載入。');s.revision=result.revision;}
    return result;
   }catch(e){if(e.status===409)s.conflict=true;if(submit&&e.status===400)s.pending=null;throw e;}finally{s.busy=false;}
  }
  async function receipt(taskId){const s=state(taskId);const r=await api(`/api/parent/tasks/${taskId}/receipt`);if(r?.taskId!==taskId||!guid.test(r.responseId)||!guid.test(r.questionnaireVersionId)||r.taskStatus!=='SUBMITTED'||!Number.isFinite(Date.parse(r.submittedAtUtc)))throw new Error('無法確認提交回執。');s.pending=null;s.revision=null;return r;}
  return Object.freeze({draft,save:(id,input)=>write(id,input,false),submit:(id,input)=>write(id,input,true),receipt});
 }
 root.EarlyCareParentQuestionnaireClient=Object.freeze({create});
})(typeof window==='undefined'?globalThis:window);
