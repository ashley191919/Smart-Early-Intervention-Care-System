"use strict";
// Shared account client; independent of the page DOM for request and race-condition tests.
(function (root) {
 function createClient(session, fetcher) {
  return async function api(path, options = {}) {
   const token = session.token();
   if (!token) throw Object.assign(new Error("請先登入家長帳號。"), {status:401});
   let response;
   try { response = await fetcher(path, {...options, headers:{...options.headers, Authorization:"Bearer " + token, "Content-Type":"application/json"}, cache:"no-store"}); }
   catch {
    if (session.token() !== token) throw Object.assign(new Error("登入身分已變更，請重新載入。"), {status:401, code:"SESSION_CHANGED"});
    // A failed write may already have committed. Never retry automatically.
    throw Object.assign(new Error("連線中斷，無法確認操作結果。請檢查網路，重新載入資料確認後再操作。"), {status:0, code:"NETWORK_UNAVAILABLE"});
   }
   if (session.token() !== token) throw Object.assign(new Error("登入身分已變更，請重新載入。"), {status:401, code:"SESSION_CHANGED"});
   if (response.status === 401) {
    session.signOut();
    throw Object.assign(new Error("登入已逾期，請重新登入。"), {status:401});
   }
   if (response.status === 204) return null;
   let body;
   try { body = await response.json(); } catch {
    if (session.token() !== token) throw Object.assign(new Error("登入身分已變更，請重新載入。"), {status:401, code:"SESSION_CHANGED"});
    const message = response.status === 403 ? "目前沒有存取權限，請重新載入或聯絡醫護人員。" : response.status === 404 ? "找不到資料，或目前已無法存取。" : "無法確認操作結果，請重新載入資料後再操作。";
    throw Object.assign(new Error(message), {status:response.status, code:"INVALID_RESPONSE"});
   }
   if (session.token() !== token) throw Object.assign(new Error("登入身分已變更，請重新載入。"), {status:401, code:"SESSION_CHANGED"});
   if (!response.ok) throw Object.assign(new Error(body.message || "暫時無法取得資料，請稍後重試。"), {status:response.status, code:body.code});
   return body;
  };
 }
 function summarize(tasks) {
  const active = tasks.filter(t => t.respondentRole === "PARENT" && t.taskStatus !== "CANCELLED");
  return { pending: active.filter(t => t.taskStatus === "PENDING").length, drafts: active.filter(t => t.taskStatus === "IN_PROGRESS").length,
   submitted: active.filter(t => t.taskStatus === "SUBMITTED").length,
   outstanding: active.filter(t => ["PENDING","IN_PROGRESS"].includes(t.taskStatus)).length };
 }
 function latestRequest() {
  let version = 0;
  return Object.freeze({ begin: () => ++version, current: value => value === version });
 }
 async function loadChildren(api) {
  const list=[];
  for(let page=1;page<=100000;page++){
   const rows=await api(`/api/parent/cases?page=${page}&pageSize=100`);
   if(!Array.isArray(rows)||rows.length>100)throw new Error("無法取得孩子資料，請重新載入。");
   list.push(...rows);if(rows.length<100)return list;
  }
  throw new Error("孩子資料過多，請聯絡承辦窗口。");
 }
 const api = Object.freeze({createClient, summarize, latestRequest,loadChildren});
 if (typeof module !== "undefined" && module.exports) module.exports = api;
 else root.EarlyCareParentClient = api;
})(typeof window === "undefined" ? globalThis : window);
