"use strict";
(()=>{
 const form=document.getElementById("authenticationForm"),status=document.getElementById("authenticationStatus"),setup=document.body.dataset.routeKey==="FirstRunSetup";let requestId=null;
 function feedback(text){status.textContent=text;}
 async function refresh(){try{feedback("جارٍ التحقق من بيانات التهيئة…");const state=await AlTamamBridge.call("GetProvisioningState");if(state.needsEnrollment!==setup){location.replace(new URL(setup?"../Login/index.html":"../FirstRunSetup/index.html",location.href));return;}if(setup)document.getElementById("recoveryKey").value=state.recoveryKey;feedback("");document.getElementById(setup?"password":"username").focus();}catch(error){feedback(error.message);form.querySelector("button[type=submit]").disabled=true;}}
 form.addEventListener("submit",async event=>{
  event.preventDefault();const button=form.querySelector("button[type=submit]"),password=document.getElementById("password").value;
  if(setup&&password!==document.getElementById("confirmPassword").value){feedback("كلمتا المرور غير متطابقتين.");return;}
  button.disabled=true;requestId??=crypto.randomUUID();feedback("جارٍ التحقق…");
  try{if(setup){await AlTamamBridge.call("CompleteProvisioning",{password,recoveryAcknowledged:document.getElementById("recoveryAcknowledged").checked},requestId);document.getElementById("recoveryKey").value="";form.reset();location.replace("../Login/index.html");}else{await AlTamamBridge.call("AuthenticateLocalUser",{username:document.getElementById("username").value.trim(),password},requestId);form.reset();location.replace("../../../index.html#dashboard");}}
  catch(error){feedback(error.message);if(error.code!=="Timeout")requestId=null;button.disabled=false;}
 });
 document.getElementById("refreshAuthentication").addEventListener("click",refresh);refresh();
})();
