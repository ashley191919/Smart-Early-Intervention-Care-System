"use strict";
(function(root){
 const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
 const decisions=["UNDECIDED","AGREED","DECLINED"];
 function create(api,uuid){
  let pending=null;
  async function load(){
   const result=await api('/api/parent/consent/context');
   if(!result||!guid.test(result.document?.documentVersionId)||!['ACCOUNT','CASE'].includes(result.document.scopeCode)||
      typeof result.document.content!=='string'||!result.document.content.trim()||!/^[0-9a-f]{64}$/i.test(result.document.contentHash)||
      !Array.isArray(result.targets)||!result.targets.length||result.targets.some(t=>!decisions.includes(t.dataUseDecision)||!decisions.includes(t.teacherParticipation)||
       (result.document.scopeCode==='CASE'?!guid.test(t.caseId):t.caseId!==null)))throw new Error('無法取得適用的同意文件，請聯絡醫療團隊。');
   return result;
  }
  async function save(document,target,dataUseDecision,teacherParticipation){
   if(!['AGREED','DECLINED'].includes(dataUseDecision)||!decisions.includes(teacherParticipation))throw new Error('請確認同意選項。');
   const input={documentVersionId:document.documentVersionId,contentHash:document.contentHash,caseId:target.caseId,dataUseDecision,teacherParticipation};
   const fingerprint=JSON.stringify(input);
   if(pending&&pending.fingerprint!==fingerprint)throw Object.assign(new Error('上次送出結果尚未確認，請重新載入同意狀態後再變更選項。'),{code:'CONSENT_OUTCOME_UNKNOWN'});
   if(!pending)pending={fingerprint,requestId:uuid()};
   const receipt=await api('/api/parent/consent',{method:'POST',body:JSON.stringify({...input,requestId:pending.requestId})});
   if(!receipt||!guid.test(receipt.consentId)||receipt.documentVersionId!==input.documentVersionId||receipt.caseId!==input.caseId||
      receipt.dataUseDecision!==input.dataUseDecision||receipt.teacherParticipation!==input.teacherParticipation)throw new Error('無法確認同意保存結果，請重新載入。');
   pending=null;return receipt;
  }
  return Object.freeze({load,save});
 }
 root.EarlyCareParentConsentClient=Object.freeze({create});
})(typeof window==='undefined'?globalThis:window);
