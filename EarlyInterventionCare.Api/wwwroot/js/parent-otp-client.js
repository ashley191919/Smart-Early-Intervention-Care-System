"use strict";
(function(root){
 function validEmail(value){return typeof value==="string"&&value.length<=254&&!/[\s<>\r\n]/.test(value)&&/^[^@]+@[^@]+$/.test(value);}
 function createClient(fetcher, now=Date.now){
  let generation=0,challenge=null,requesting=false,verifying=false;
  async function send(path,body){
   let response;
   try{response=await fetcher(path,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body),cache:"no-store",credentials:"omit"});}
   catch{throw new Error("無法連線，請確認網路後再試。若已收到驗證碼，請先確認目前畫面狀態。");}
   let data;try{data=await response.json();}catch{throw Object.assign(new Error(response.status===429?"操作過於頻繁，請稍後再試。":"登入服務暫時無法使用，請稍後重試。"),{status:response.status});}
   if(!response.ok)throw Object.assign(new Error(data.message||"無法完成驗證，請確認輸入或稍後重試。"),{status:response.status,code:data.code});
   return data;
  }
  return Object.freeze({
   async request(email){
    email=email.trim();if(!validEmail(email))throw new Error("請輸入有效的電子郵件地址。");
    if(requesting||verifying)throw Object.assign(new Error("正在處理，請稍候。"),{code:"BUSY"});
    const current=++generation;challenge=null;requesting=true;
    try{
     const result=await send("/api/parent/auth/email/request",{email});
     if(current!==generation)throw Object.assign(new Error("操作已取消。"),{code:"FLOW_CHANGED"});
     if(typeof result.challengeId!=="string"||!result.challengeId||!Number.isFinite(Date.parse(result.expiresAtUtc))||Date.parse(result.expiresAtUtc)<=now()||!Number.isInteger(result.resendAfterSeconds)||result.resendAfterSeconds<0||result.resendAfterSeconds>3600)throw new Error("無法取得有效驗證資訊，請重新取得。");
     challenge=Object.freeze({...result});return challenge;
    }finally{if(current===generation)requesting=false;}
   },
   async verify(code){
    if(requesting||verifying)throw Object.assign(new Error("正在處理，請稍候。"),{code:"BUSY"});
    if(!challenge||Date.parse(challenge.expiresAtUtc)<=now())throw new Error("驗證碼已過期，請重新取得。");
    if(!/^[0-9]{6}$/.test(code))throw new Error("請輸入 6 位數驗證碼。");
    const current=generation,id=challenge.challengeId;verifying=true;
    try{
     const result=await send("/api/parent/auth/email/verify",{challengeId:id,code});
     if(current!==generation)throw Object.assign(new Error("操作已取消。"),{code:"FLOW_CHANGED"});
     if(result.role!=="PARENT"||typeof result.accessToken!=="string"||!result.accessToken.trim())throw new Error("無法取得有效登入結果。");
     challenge=null;return result;
    }finally{if(current===generation)verifying=false;}
   },
   reset(){generation++;challenge=null;requesting=false;verifying=false;}
  });
 }
 function mount(window,document){
  const emailForm=document.querySelector("#parent-form"),otpForm=document.querySelector("#parent-otp-form");
  const email=document.querySelector("#parent-email"),code=document.querySelector("#parent-otp"),emailError=document.querySelector("#email-error"),otpError=document.querySelector("#parent-otp-error"),resend=document.querySelector("#parent-resend"),expiry=document.querySelector("#parent-otp-expiry");
  const initialSubmit=emailForm.querySelector('[type="submit"]'),verifySubmit=otpForm.querySelector('[type="submit"]');
  const client=createClient(window.fetch.bind(window));let timer=null,address="",expires=0,resendAt=0,viewGeneration=0,sending=false,verifying=false;
  function showError(field,target,message){target.textContent=message;target.hidden=false;field.setAttribute("aria-invalid","true");field.focus();}
  function clearError(field,target){field.removeAttribute("aria-invalid");target.hidden=true;target.textContent="";}
  function clock(){const left=Math.max(0,Math.ceil((expires-Date.now())/1000)),wait=Math.max(0,Math.ceil((resendAt-Date.now())/1000));expiry.textContent=left?`驗證碼有效時間：${Math.floor(left/60)} 分 ${String(left%60).padStart(2,"0")} 秒`:"驗證碼已過期，請重新取得。";resend.disabled=wait>0||sending||verifying;resend.textContent=wait?`重新取得（${wait} 秒）`:"重新取得驗證碼";verifySubmit.disabled=sending||verifying||left===0;}
  function reset(){viewGeneration++;client.reset();clearInterval(timer);timer=null;emailForm.hidden=false;otpForm.hidden=true;emailForm.reset();otpForm.reset();clearError(email,emailError);clearError(code,otpError);address="";expires=0;resendAt=0;sending=false;verifying=false;initialSubmit.disabled=false;verifySubmit.disabled=false;resend.disabled=true;expiry.textContent="";document.querySelector("#parent-masked-email").textContent="";}
  async function request(){
   const version=viewGeneration;const result=await client.request(address);if(version!==viewGeneration)return;
   expires=Date.parse(result.expiresAtUtc);resendAt=Date.now()+result.resendAfterSeconds*1000;clearInterval(timer);clock();timer=setInterval(clock,1000);emailForm.hidden=true;otpForm.hidden=false;code.value="";clearError(code,otpError);
   const at=address.lastIndexOf("@");document.querySelector("#parent-masked-email").textContent=`${address.slice(0,1)}***${address.slice(at)}`;code.focus();
  }
  email.addEventListener("input",()=>clearError(email,emailError));code.addEventListener("input",()=>clearError(code,otpError));
  emailForm.addEventListener("submit",async event=>{
   event.preventDefault();if(sending||verifying)return;address=email.value.trim();
   if(!validEmail(address)||!email.checkValidity()){showError(email,emailError,"請輸入有效的電子郵件地址。");return;}
   const version=viewGeneration;sending=true;initialSubmit.disabled=true;clearError(email,emailError);
   try{await request();}catch(error){if(version===viewGeneration)showError(email,emailError,error.message);}
   finally{if(version===viewGeneration){sending=false;initialSubmit.disabled=false;if(!otpForm.hidden)clock();}}
  });
  otpForm.addEventListener("submit",async event=>{
   event.preventDefault();if(sending||verifying)return;const version=viewGeneration;verifying=true;clock();clearError(code,otpError);
   try{const result=await client.verify(code.value.trim());if(version===viewGeneration)window.EarlyCareParentSession.signIn(result);}
   catch(error){if(version===viewGeneration)showError(code,otpError,error.message);}
   finally{if(version===viewGeneration){verifying=false;clock();}}
  });
  resend.addEventListener("click",async()=>{
   if(sending||verifying||Date.now()<resendAt)return;const version=viewGeneration;sending=true;clock();clearError(code,otpError);
   try{await request();}catch(error){if(version===viewGeneration){expires=0;resendAt=Date.now()+60000;showError(code,otpError,error.message);}}
   finally{if(version===viewGeneration){sending=false;clock();}}
  });
  document.querySelector("#parent-change-email").addEventListener("click",()=>{reset();email.focus();});
  document.addEventListener("login-role-change",reset);window.addEventListener("pagehide",reset);
 }
 const api=Object.freeze({createClient,mount});if(typeof module!=="undefined"&&module.exports)module.exports=api;else root.EarlyCareParentOtp=api;
})(typeof window==="undefined"?globalThis:window);
