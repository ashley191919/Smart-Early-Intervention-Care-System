"use strict";
(function(root){
 function createClient(fetcher, now=Date.now){
  let generation=0, challenge=null;
  async function send(path,body){
   const response=await fetcher(path,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body),cache:"no-store",credentials:"omit"});
   let data;try{data=await response.json();}catch{throw new Error("登入服務暫時無法使用，請稍後重試。");}
   if(!response.ok)throw Object.assign(new Error(data.message||"無法完成驗證，請確認輸入或稍後重試。"),{status:response.status,code:data.code});
   return data;
  }
  return Object.freeze({
   async request(phone){
    const current=++generation;
    const result=await send("/api/parent/auth/otp/request",{phone});
    if(current!==generation)throw Object.assign(new Error("操作已取消。"),{code:"FLOW_CHANGED"});
    if(typeof result.challengeId!=="string"||!result.challengeId||!Number.isFinite(Date.parse(result.expiresAtUtc))||Date.parse(result.expiresAtUtc)<=now()||!Number.isInteger(result.resendAfterSeconds)||result.resendAfterSeconds<0||result.resendAfterSeconds>3600)throw new Error("無法取得有效驗證資訊，請重新取得。");
    challenge=Object.freeze({...result});return challenge;
   },
   async verify(code){
    if(!challenge||Date.parse(challenge.expiresAtUtc)<=now())throw new Error("驗證碼已過期，請重新取得。");
    if(!/^[0-9]{6}$/.test(code))throw new Error("請輸入 6 位數驗證碼。");
    const current=generation,id=challenge.challengeId;
    const result=await send("/api/parent/auth/otp/verify",{challengeId:id,code});
    if(current!==generation)throw Object.assign(new Error("操作已取消。"),{code:"FLOW_CHANGED"});
    if(result.role!=="PARENT"||typeof result.accessToken!=="string"||!result.accessToken.trim())throw new Error("無法取得有效登入結果。");
    challenge=null;generation++;return result;
   },
   reset(){generation++;challenge=null;}
  });
 }
 function mount(window,document){
  const phoneForm=document.querySelector("#parent-form"),otpForm=document.querySelector("#parent-otp-form");
  const phone=document.querySelector("#parent-phone"),code=document.querySelector("#parent-otp"),phoneError=document.querySelector("#phone-error"),otpError=document.querySelector("#parent-otp-error"),resend=document.querySelector("#parent-resend"),expiry=document.querySelector("#parent-otp-expiry");
  const client=createClient(window.fetch.bind(window));let timer=null,number="",expires=0,resendAt=0,viewGeneration=0;
  document.querySelector(".parent-demo-code").hidden=true;
  phoneForm.querySelector(".workspace-entry a").href="/parent-workspace.html?mode=account";
  function reset(){viewGeneration++;client.reset();clearInterval(timer);phoneForm.hidden=false;otpForm.hidden=true;phoneForm.reset();otpForm.reset();phoneError.hidden=true;otpError.hidden=true;number="";}
  function clock(){const left=Math.max(0,Math.ceil((expires-Date.now())/1000)),wait=Math.max(0,Math.ceil((resendAt-Date.now())/1000));expiry.textContent=left?`驗證碼有效時間：${Math.floor(left/60)} 分 ${String(left%60).padStart(2,"0")} 秒`:"驗證碼已過期，請重新取得。";resend.disabled=wait>0;resend.textContent=wait?`重新取得（${wait} 秒）`:"重新取得驗證碼";}
  async function request(){const request=viewGeneration;const result=await client.request(number);if(request!==viewGeneration)return;expires=Date.parse(result.expiresAtUtc);resendAt=Date.now()+result.resendAfterSeconds*1000;clearInterval(timer);clock();timer=setInterval(clock,1000);phoneForm.hidden=true;otpForm.hidden=false;code.value="";otpError.hidden=true;document.querySelector("#parent-masked-phone").textContent=`${number.slice(0,4)}-***-${number.slice(-3)}`;code.focus();}
  phoneForm.addEventListener("submit",async event=>{event.preventDefault();number=phone.value.replace(/[\s-]/g,"").replace(/^\+886/,"0");if(!/^09[0-9]{8}$/.test(number)){phoneError.textContent="請輸入 09 開頭的 10 碼手機號碼。";phoneError.hidden=false;return;}const button=phoneForm.querySelector('[type="submit"]'),requestVersion=viewGeneration;button.disabled=true;phoneError.hidden=true;try{await request();}catch(error){if(requestVersion===viewGeneration){phoneError.textContent=error.message;phoneError.hidden=false;}}finally{button.disabled=false;}});
  otpForm.addEventListener("submit",async event=>{event.preventDefault();const button=otpForm.querySelector('[type="submit"]'),requestVersion=viewGeneration;button.disabled=true;otpError.hidden=true;try{const result=await client.verify(code.value.trim());if(requestVersion===viewGeneration)window.EarlyCareParentSession.signIn(result);}catch(error){if(requestVersion===viewGeneration){otpError.textContent=error.message;otpError.hidden=false;}}finally{button.disabled=false;}});
  resend.addEventListener("click",async()=>{if(Date.now()<resendAt)return;const requestVersion=viewGeneration;resend.disabled=true;try{await request();}catch(error){if(requestVersion===viewGeneration){otpError.textContent=error.message;otpError.hidden=false;resend.disabled=false;}}});
  document.querySelector("#parent-change-phone").addEventListener("click",reset);
  document.addEventListener("login-role-change",reset);window.addEventListener("pagehide",reset);
 }
 const api=Object.freeze({createClient,mount});if(typeof module!=="undefined"&&module.exports)module.exports=api;else root.EarlyCareParentOtp=api;
})(typeof window==="undefined"?globalThis:window);
