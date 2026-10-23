"use strict";
(()=>{
 const pending=new Map();let nonce="";const transport=window.chrome?.webview;
 transport?.addEventListener("message",event=>{
  const response=event.data;if(!response||typeof response.requestId!=="string")return;
  const entry=pending.get(response.requestId);if(!entry)return;pending.delete(response.requestId);clearTimeout(entry.timer);
  if(response.success===true){if(response.data?.sessionNonce)nonce=response.data.sessionNonce;for(const detail of response.invalidationEvents||[])window.dispatchEvent(new CustomEvent("altamam:invalidate",{detail}));entry.resolve(response.data);}
  else{const error=new Error(response.userMessage||"تعذر تنفيذ الطلب.");error.code=response.errorCode;entry.reject(error);}
 });
 async function call(action,payload={},requestId=crypto.randomUUID()){
  if(!transport)throw new Error("افتح هذه الواجهة من برنامج التمام اليومي على Windows.");
  if(pending.has(requestId))throw new Error("الطلب قيد التنفيذ.");
  return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(requestId);const error=new Error("تأخر الرد؛ تحقق من النتيجة قبل إعادة المحاولة.");error.code="Timeout";reject(error);},30000);pending.set(requestId,{resolve,reject,timer});transport.postMessage({requestId,protocolVersion:1,action,sessionNonce:nonce,payload});});
 }
 async function session(){const state=await call("GetCurrentSession");nonce=state.sessionNonce;return state;}
 window.AlTamamBridge=Object.freeze({call,session,connected:!!transport});
})();
