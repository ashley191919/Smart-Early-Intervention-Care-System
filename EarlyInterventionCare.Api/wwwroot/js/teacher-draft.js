'use strict';
// DB draft transport/state only. Cookies are browser-managed; never store credentials or answers on disk.
(function(root){
  class DraftApiError extends Error {
    constructor(message,code='DRAFT_UNAVAILABLE',status=0){super(message);this.code=code;this.status=status;}
  }
  const revision=value=>{
    const raw=String(value);
    if(!/^\d+$/.test(raw)||typeof value==='number'&&!Number.isSafeInteger(value)||BigInt(raw)>18446744073709551615n)
      throw new DraftApiError('草稿版本無效，請重新載入。');
    return BigInt(raw).toString();
  };
  async function readResponse(response){
    // Preserve ulong revision precision beyond JavaScript's safe integer range.
    const raw=await response.text();
    let body;
    try{body=JSON.parse(raw.replace(/("revision"\s*:\s*)(\d+)(?=\s*[,}])/g,'$1"$2"'));}
    catch{throw new DraftApiError('無法確認草稿結果，請重新載入最新草稿。');}
    if(!response.ok)throw new DraftApiError(body.message||'暫時無法讀寫草稿。',body.code||'DRAFT_UNAVAILABLE',response.status);
    return body;
  }
  function decode(form,body){
    if(body.taskId?.toLowerCase()!==form.taskId.toLowerCase()||body.questionnaireVersionId?.toLowerCase()!==form.versionId.toLowerCase()||
       !['PENDING','IN_PROGRESS'].includes(body.taskStatus)||!Array.isArray(body.answers))
      throw new DraftApiError('草稿與目前任務不符，請重新載入。');
    const rev=revision(body.revision),answers={};
    for(const answer of body.answers){
      const index=form.questionIds.indexOf(answer?.questionId),option=form.optionValues.indexOf(answer?.optionValue);
      if(index<0||option<0||Object.hasOwn(answers,index))throw new DraftApiError('草稿答案不符合指定版本。');
      answers[index]=option;
    }
    const savedAt=body.updatedAtUtc==null?null:Date.parse(body.updatedAtUtc);
    if(savedAt!==null&&!Number.isFinite(savedAt)||rev!=='0'&&savedAt===null)
      throw new DraftApiError('草稿保存時間無效。');
    return {taskId:form.taskId,answers,respondent:body.respondentName||'',date:body.filledOn||'',note:body.observation||'',
      savedAt,submittedAt:null,revision:rev,taskStatus:body.taskStatus};
  }
  class TeacherDraftClient {
    constructor(fetcher){this.fetcher=fetcher;this.records=new Map();}
    record(form){
      if(!this.records.has(form.taskId))this.records.set(form.taskId,{loaded:false,busy:false,conflict:false,revision:null,phase:'unloaded',snapshot:null});
      return this.records.get(form.taskId);
    }
    async load(form){
      const record=this.record(form);
      if(record.busy)throw new DraftApiError('正在讀寫草稿，請稍候。','DRAFT_BUSY');
      record.busy=true;record.phase='loading';
      try{
        const body=await readResponse(await this.fetcher(`/api/dev/teacher-workspace/tasks/${form.taskId}/draft`,
          {credentials:'same-origin',cache:'no-store'}));
        const snapshot=decode(form,body);
        Object.assign(record,{loaded:true,conflict:false,revision:snapshot.revision,snapshot,phase:'clean'});
        return snapshot;
      }catch(error){record.phase=record.conflict?'conflict':'error';throw error;}
      finally{record.busy=false;}
    }
    async save(form,input){
      const record=this.record(form);
      if(record.busy)throw new DraftApiError('正在讀寫草稿，請稍候。','DRAFT_BUSY');
      if(!record.loaded)throw new DraftApiError('請先載入 DB 草稿，才能保存。','DRAFT_NOT_LOADED');
      if(record.conflict)throw new DraftApiError('草稿已有較新版本，請先重新載入。','DRAFT_CONFLICT',409);
      const answers=Object.entries(input.answers).map(([index,option])=>{
        if(!Number.isInteger(Number(index))||!Number.isInteger(option)||form.questionIds[index]===undefined||form.optionValues[option]===undefined)
          throw new DraftApiError('答案無法對應指定問卷。','INVALID_ANSWERS',400);
        return {questionId:form.questionIds[index],optionValue:form.optionValues[option]};
      });
      const payload={questionnaireVersionId:form.versionId,expectedRevision:record.revision,
        respondentName:input.respondent||null,filledOn:input.date||null,answers,observation:input.note||null};
      const body=JSON.stringify(payload).replace(/"expectedRevision":"(\d+)"/,'"expectedRevision":$1');
      record.busy=true;record.phase='saving';
      try{
        const result=await readResponse(await this.fetcher(`/api/dev/teacher-workspace/tasks/${form.taskId}/draft`,
          {method:'PUT',credentials:'same-origin',cache:'no-store',headers:{'Content-Type':'application/json','X-Teacher-Draft':'1'},body}));
        const snapshot=decode(form,result);
        if(BigInt(snapshot.revision)!==BigInt(record.revision)+1n)throw new DraftApiError('無法確認草稿版本，請重新載入。');
        Object.assign(record,{revision:snapshot.revision,snapshot,phase:'clean'});
        return snapshot;
      }catch(error){
        if(error.code==='DRAFT_CONFLICT')record.conflict=true;
        record.phase=record.conflict?'conflict':'error';throw error;
      }finally{record.busy=false;}
    }
  }
  root.TeacherDraftClient=TeacherDraftClient;
  if(typeof module!=='undefined'&&module.exports)module.exports={TeacherDraftClient,DraftApiError};
})(globalThis);
