"use strict";
(()=>{
 const bridge=window.AlTamamBridge,esc=v=>String(v??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[c]));
 window.addEventListener("DOMContentLoaded",async()=>{
  const mount=document.getElementById("correctionMount");if(!mount)return;
  let busy=false,attempt=null;
  function notice(text){mount.querySelector("#correctionFeedback").textContent=text;}
  async function perform(action){if(busy)return;busy=true;const locked=[...mount.querySelectorAll("input,select,button")].map(e=>[e,e.disabled]);locked.forEach(([e])=>e.disabled=true);try{await action();}catch(e){notice(e.message);}finally{busy=false;locked.forEach(([e,disabled])=>e.disabled=disabled);}}
  try{
   const session=await bridge.session(),state=await bridge.call("GetAutoAttendanceRun",{});
   if(!state.canCorrectClosedDay){mount.textContent="التصحيح الاستثنائي غير متاح. يلزم تفعيله في إعدادات البرنامج وتسجيل الدخول بحساب الإدارة العامة.";return;}
   const statuses=await bridge.call("GetActiveAttendanceStatuses",{});
   mount.innerHTML=`<p>تصحيح حالة معتمدة في يوم سابق مع حفظ الحالة الأصلية وسبب التصحيح في السجل.</p><div id="correctionFeedback" role="status" aria-live="polite"></div><form id="correctionSearchForm" class="registry-toolbar"><label>الاسم أو الرقم (ع)<input id="correctionSearch" class="form-control" type="search" maxlength="100" required></label><button class="btn btn-outline">بحث</button></form><form id="correctionForm" class="registry-grid"><label>الفرد<select id="correctionPerson" class="form-select" required><option value="">ابحث عن الفرد أولاً</option></select></label><label>التاريخ<input id="correctionDate" class="form-control" type="date" max="${esc(session.businessDate)}" required></label><label>الحالة الجديدة<select id="correctionStatus" class="form-select" required>${statuses.map(s=>`<option value="${esc(s.statusId)}">${esc(s.displayName)}</option>`).join("")}</select></label><label>سبب التصحيح<input id="correctionReason" class="form-control" maxlength="500" required></label><button class="btn btn-primary">معاينة التصحيح</button></form>`;
   mount.querySelector("#correctionSearchForm").onsubmit=e=>{e.preventDefault();perform(async()=>{const found=await bridge.call("QueryPersons",{search:mount.querySelector("#correctionSearch").value,includeInactive:true,limit:50});const select=mount.querySelector("#correctionPerson");select.replaceChildren(new Option("اختر الفرد",""));found.items.forEach(p=>select.add(new Option(p.name+" — "+p.serviceNumberA,p.personId)));notice(found.total>50?"توجد نتائج إضافية؛ خصص البحث بالرقم (ع).":`عدد النتائج: ${found.total}`);});};
   mount.querySelector("#correctionForm").onsubmit=e=>{e.preventDefault();perform(async()=>{
    const change={personId:mount.querySelector("#correctionPerson").value,businessDate:mount.querySelector("#correctionDate").value,statusId:mount.querySelector("#correctionStatus").value,reason:mount.querySelector("#correctionReason").value};
    const signature=JSON.stringify(change);
    // A timed-out commit is retried with exactly the original payload and request ID.
    if(!attempt||attempt.signature!==signature){const preview=await bridge.call("PreviewClosedDayCorrection",change);const dialog=document.createElement("dialog");dialog.className="registry-confirm";dialog.innerHTML=`<h2>تأكيد التصحيح الاستثنائي</h2><p>${esc(preview.personName)} — ${esc(preview.serviceNumberA)}</p><p>${esc(preview.siteName)} — ${esc(change.businessDate)}</p><p>الحالة السابقة: ${esc(preview.beforeStatus)}؛ الجديدة: ${esc(preview.afterStatus)}</p><p>السبب: ${esc(change.reason)}</p><button class="btn btn-primary" id="correctionAccept">اعتماد</button><button class="btn btn-outline" id="correctionCancel">إلغاء</button>`;document.body.append(dialog);dialog.showModal();dialog.querySelector("#correctionCancel").focus();const accepted=await new Promise(resolve=>{dialog.querySelector("#correctionAccept").onclick=()=>dialog.close("yes");dialog.querySelector("#correctionCancel").onclick=()=>dialog.close();dialog.addEventListener("close",()=>{resolve(dialog.returnValue==="yes");dialog.remove();},{once:true});});if(!accepted)return;attempt={signature,id:crypto.randomUUID(),payload:{change,previewHash:preview.previewHash}};}
    try{await bridge.call("CommitClosedDayCorrection",attempt.payload,attempt.id);attempt=null;notice("تم اعتماد التصحيح وحفظ دليل الحالة السابقة وسبب التعديل.");}catch(error){if(error.code!=="Timeout")attempt=null;throw error;}
   });};
  }catch(e){mount.textContent=e.message;}
 });
})();
