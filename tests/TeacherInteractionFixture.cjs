'use strict';
// Disposable loopback-only UI fixture. No real credentials, database or production registration.
const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../EarlyInterventionCare.Api/wwwroot');
const forms=[0,1].map(i=>({id:i?'second':'first',taskId:`40000000-0000-4000-8000-00000000000${i+1}`,
 versionId:`30000000-0000-4000-8000-00000000000${i+1}`,title:i?'第二份測試問卷':'第一份測試問卷',desc:'虛構測試題目',
 options:['沒有','偶爾','經常'],optionValues:['0','1','2'],questions:['測試觀察一','測試觀察二'],questionIds:['Q1','Q2'],
 instructions:'僅供隔離操作測試',source:'Synthetic fixture',groups:{},color:''}));
let mode='normal',drafts,receipts,submitKeys,saveCount,submitCount,logoutCount;
function reset(){mode='normal';drafts=new Map(forms.map(f=>[f.taskId,{taskId:f.taskId,questionnaireVersionId:f.versionId,revision:0,
 respondentName:null,filledOn:null,answers:[],observation:null,updatedAtUtc:null,taskStatus:'PENDING'}]));
 receipts=new Map();submitKeys=[];saveCount=0;submitCount=0;logoutCount=0;}
reset();
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://127.0.0.1');
 const json=(data,status=200)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end(JSON.stringify(data));};
 try{
  if(url.pathname==='/fixture/start'){reset();res.writeHead(302,{'Location':'/teacher-workspace.html?mode=authorized','Set-Cookie':'EarlyCare.DevTeacher=fixture-only; HttpOnly; SameSite=Strict; Path=/api/dev/teacher-workspace'});return res.end();}
  if(url.pathname.startsWith('/fixture/mode/')){const next=url.pathname.split('/').pop();if(!['normal','deny','conflict','savefail','uncertain','malformed'].includes(next))return json({},400);mode=next;return json({mode});}
  if(url.pathname==='/fixture/stats')return json({mode,saveCount,submitCount,logoutCount,submitKeys,responseCount:receipts.size});
  if(url.pathname.startsWith('/api/dev/teacher-workspace')){
   if(url.pathname.endsWith('/logout')){logoutCount++;res.writeHead(204,{'Set-Cookie':'EarlyCare.DevTeacher=; Max-Age=0; Path=/api/dev/teacher-workspace'});return res.end();}
   if(mode==='deny'||!req.headers.cookie?.includes('EarlyCare.DevTeacher=fixture-only')){res.writeHead(401,{'Content-Type':'text/html'});return res.end('Denied fixture');}
   if(url.pathname.endsWith('/task'))return json({taskId:forms[0].taskId,grantId:'50000000-0000-4000-8000-000000000001',grantStatus:receipts.size===forms.length?'USED':'ACTIVE',
    patient:{displayName:'虛構測試孩子',caseCode:'QA-ONLY',sex:'未提供',age:4},questionnaires:forms});
   const match=url.pathname.match(/\/tasks\/([^/]+)\/(draft|submit)$/),form=forms.find(f=>f.taskId===match?.[1]);
   if(!match||!form)return json({},404);
   if(match[2]==='draft'&&req.method==='GET')return json(drafts.get(form.taskId));
   const chunks=[];for await(const chunk of req)chunks.push(chunk);const body=JSON.parse(Buffer.concat(chunks).toString());
   if(match[2]==='draft'){
    saveCount++;const old=drafts.get(form.taskId);
    if(mode==='savefail')return json({message:'暫時無法儲存，輸入仍保留。'},503);
    if(mode==='conflict'){mode='normal';drafts.set(form.taskId,{...old,revision:old.revision+1,respondentName:'另一位虛構測試填答者',answers:[{questionId:'Q1',optionValue:'2'}],updatedAtUtc:new Date().toISOString(),taskStatus:'IN_PROGRESS'});return json({code:'DRAFT_CONFLICT',message:'草稿已有較新版本'},409);}
    if(body.expectedRevision!==old.revision)return json({code:'DRAFT_CONFLICT',message:'草稿版本衝突'},409);
    const next={...old,...body,revision:old.revision+1,updatedAtUtc:new Date().toISOString(),taskStatus:'IN_PROGRESS'};drafts.set(form.taskId,next);return json(next);
   }
   submitCount++;const key=req.headers['idempotency-key'];submitKeys.push(key);
   if(mode==='malformed')return json({message:'missing receipt fields'});
   const previous=receipts.get(form.taskId);if(previous)return json({...previous,replayed:true});
   const receipt={responseId:`60000000-0000-4000-8000-00000000000${receipts.size+1}`,taskId:form.taskId,questionnaireVersionId:form.versionId,
    submittedAtUtc:new Date().toISOString(),grantStatus:receipts.size+1===forms.length?'USED':'ACTIVE',replayed:false};
   receipts.set(form.taskId,receipt);
   if(mode==='uncertain'){mode='normal';return json({message:'無法確認提交結果，請重試。'},503);}
   return json(receipt);
  }
  const file=path.resolve(root,'.'+decodeURIComponent(url.pathname==='/'?'/index.html':url.pathname));
  if(!file.startsWith(root+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile())return json({},404);
  const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.png':'image/png'};
  res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store'});fs.createReadStream(file).pipe(res);
 }catch{if(!res.headersSent)res.writeHead(500);res.end();}
});
server.listen(5184,'127.0.0.1',()=>console.log('Disposable teacher fixture: http://127.0.0.1:5184/fixture/start'));
