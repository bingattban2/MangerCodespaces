"use strict";
(()=>{
 const bridge=window.AlTamamBridge;
 const esc=v=>String(v??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[c]));
 const option=(id,text,selected=false)=>`<option value="${esc(id)}"${selected?" selected":""}>${esc(text)}</option>`;
 async function start(){
  const mount=document.getElementById("attendanceMount");if(!mount||mount.dataset.started)return;mount.dataset.started="true";
  let session,sites=[],statuses=[],rows={items:[],total:0},summary,chosen=new Set(),draftId=null,busy=false,attempt=null,notice="",error=false,sequence=0;
  let filter={search:"",siteId:null,statusId:null,unpreparedOnly:["UnpreparedPersonnel","SelectStatusAndConfirm"].includes(document.body.dataset.routeKey),offset:0,limit:50};
  let target="",revise=false,reason="",all=false;
  const draftsOnly=document.body.dataset.routeKey==="DraftsAndTemporarySave";
  function feedback(text,failed=false){notice=text;error=failed;const e=mount.querySelector("#attendanceFeedback");if(e){e.textContent=text;e.dataset.error=String(failed);}}
  async function confirm(title,body){
   const d=document.createElement("dialog");d.className="registry-confirm";d.setAttribute("aria-labelledby","attendanceConfirmTitle");
   d.innerHTML=`<h2 id="attendanceConfirmTitle">${esc(title)}</h2><p class="attendance-preview">${esc(body)}</p><div class="registry-actions"><button id="attendanceConfirmCommit" class="btn btn-primary">اعتماد</button><button id="attendanceConfirmCancel" class="btn btn-outline">إلغاء</button></div>`;
   document.body.append(d);d.showModal();d.querySelector("#attendanceConfirmCancel").focus();return new Promise(resolve=>{d.addEventListener("cancel",e=>{e.preventDefault();d.close("cancel");});d.querySelector("#attendanceConfirmCommit").onclick=()=>d.close("commit");d.querySelector("#attendanceConfirmCancel").onclick=()=>d.close("cancel");d.addEventListener("close",()=>{const ok=d.returnValue==="commit";d.remove();resolve(ok);},{once:true});});
  }
  function change(){return {businessDate:session.businessDate,selection:{personIds:draftId||all?[]:[...chosen],allMatching:all,filter,draftId},statusId:target,revise,reason};}
  async function mutate(action,payload){
   const key=JSON.stringify({action,payload});if(!attempt||attempt.key!==key)attempt={key,id:crypto.randomUUID()};
   try{const result=await bridge.call(action,payload,attempt.id);attempt=null;return result;}catch(e){if(e.code!=="Timeout")attempt=null;throw e;}
  }
  async function perform(task){if(busy)return;busy=true;feedback("");mount.setAttribute("aria-busy","true");const disabled=[...mount.querySelectorAll("button,input,select")].map(e=>[e,e.disabled]);disabled.forEach(([e])=>e.disabled=true);try{await task();}catch(e){feedback(e.message,true);}finally{busy=false;mount.removeAttribute("aria-busy");disabled.forEach(([e,v])=>{if(e.isConnected)e.disabled=v;});}}
  const root=new URL("../../",document.querySelector('script[src$="/bridge-client.js"]').src);
  function stats(){return `<div class="counters-grid">${[["إجمالي القوة",summary.rosterCount],["القوة المستحقة للتحضير",summary.eligibleCount],["يوم نقل — مستثنى",summary.movementExemptCount],["المحضرون",summary.preparedCount],["غير المحضرين",summary.unpreparedCount],...statuses.map(s=>[s.displayName,summary.statusCounts.filter(c=>c.id===s.statusId).reduce((a,c)=>a+c.count,0),({"STS-PRESENT":"present","STS-LEAVE":"leave","STS-ABSENT":"absent","STS-PERMISSION":"permission"}[s.statusId]||"navy")]),...summary.statusCounts.filter(c=>!statuses.some(s=>s.statusId===c.id)).map(c=>[c.caption+" (حالة معطلة)",c.count,"navy"])].map(([label,n,kind])=>`<div class="counter-card counter-kind-${kind||"navy"}"><div class="counter-label">${esc(label)}</div><div class="counter-value">${n}</div></div>`).join("")}</div><p class="registry-help">غير المحضرين لم تُعتمد لهم حالة بعد. نسبة اكتمال التحضير: ${summary.eligibleCount?Math.round(summary.preparedCount*100/summary.eligibleCount):0}%</p>`;}
  function render(){
   mount.innerHTML=`<div id="attendanceFeedback" class="registry-feedback" role="status" aria-live="polite" data-error="${error}">${esc(notice)}</div><p>يوم العمل: <strong id="attendanceBusinessDate">${esc(session.businessDate)}</strong></p>${stats()}<div class="chips-row"><a class="filter-chip" href="${new URL("screens/Dashboard/UnpreparedPersonnel/index.html",root).href}">فتح غير المحضرين</a><button class="filter-chip" data-status-chip="">الكل</button>${statuses.map(s=>`<button class="filter-chip${filter.statusId===s.statusId?" active":""}" data-status-chip="${esc(s.statusId)}">${esc(s.shortSymbol)} — ${esc(s.displayName)}</button>`).join("")}</div>
   <form id="attendanceFilter" class="registry-toolbar"><label>الاسم أو الرقم (ع)<input class="form-control" id="attendanceSearch" type="search" maxlength="100" value="${esc(filter.search)}"></label><label>الموقع<select id="attendanceSite" class="form-select">${option("","جميع المواقع")+sites.map(s=>option(s.siteId,s.displayName,s.siteId===filter.siteId)).join("")}</select></label><label>الحالة<select id="attendanceStatusFilter" class="form-select">${option("","جميع الحالات")+statuses.map(s=>option(s.statusId,s.displayName,s.statusId===filter.statusId)).join("")}</select></label><label><input id="attendanceUnprepared" type="checkbox"${filter.unpreparedOnly?" checked":""}>غير المحضرين فقط</label><button class="btn btn-outline">بحث وتحديث</button></form>
   <p class="registry-help">${draftId?"تم تحميل مسودة. أعد المعاينة للتحقق من بياناتها الحالية.":"اختر أفراداً أو جميع النتائج المطابقة للبحث."}</p><label><input id="attendanceAll" type="checkbox"${all?" checked":""}>اختيار جميع النتائج المطابقة (${rows.total})</label>
   <div class="table-wrap"><table class="data-table"><thead><tr><th>اختيار</th><th>الرقم (ع)</th><th>الاسم</th><th>الموقع</th><th>حالة اليوم</th><th>حالة الأمس</th></tr></thead><tbody>${rows.items.map(p=>`<tr><td><input type="checkbox" data-attendance-person="${esc(p.personId)}" aria-label="اختيار ${esc(p.name)}"${chosen.has(p.personId)||all?" checked":""}${p.movementExempt||all?" disabled":""}></td><td>${esc(p.serviceNumberA)}</td><td>${esc(p.name)}</td><td>${esc(p.siteName)}</td><td>${esc(p.movementExempt?"يوم نقل — مستثنى":p.statusName||"غير محضر")}</td><td>${esc(p.yesterdayStatus||"لم يُحضّر")}</td></tr>`).join("")||'<tr><td colspan="6">لا توجد نتائج مطابقة.</td></tr>'}</tbody></table></div>
   <div class="registry-actions"><button id="attendancePrevious" class="btn btn-outline"${filter.offset===0?" disabled":""}>السابق</button><span>${rows.total?filter.offset+1:0}–${filter.offset+rows.items.length} من ${rows.total}</span><button id="attendanceNext" class="btn btn-outline"${filter.offset+rows.items.length>=rows.total?" disabled":""}>التالي</button></div>
   <form id="attendanceForm"><fieldset class="registry-section"><legend>اعتماد التحضير</legend><div class="registry-grid"><label>الحالة المطلوبة<select id="attendanceTarget" class="form-select" required>${option("","اختر الحالة")+statuses.map(s=>option(s.statusId,s.displayName,s.statusId===target)).join("")}</select></label><label><input id="attendanceRevise" type="checkbox"${revise?" checked":""}>تعديل تحضير اليوم</label><label>سبب التعديل<input id="attendanceReason" class="form-control" maxlength="500" value="${esc(reason)}"${revise?" required":""}></label></div><div class="registry-actions"><button class="btn btn-primary">معاينة التحضير</button><button type="button" id="attendanceSaveDraft" class="btn btn-outline">حفظ مسودة</button><button type="button" id="attendanceDrafts" class="btn btn-outline">المسودات</button></div></fieldset></form><div id="attendanceDraftList"></div>`;
   mount.querySelectorAll("[data-status-chip]").forEach(e=>e.onclick=()=>perform(async()=>{filter={...filter,statusId:e.dataset.statusChip||null,unpreparedOnly:false,offset:0};chosen.clear();all=false;draftId=null;await load();}));
   mount.querySelector("#attendanceFilter").onsubmit=e=>{e.preventDefault();filter={...filter,search:mount.querySelector("#attendanceSearch").value,siteId:mount.querySelector("#attendanceSite").value||null,statusId:mount.querySelector("#attendanceStatusFilter").value||null,unpreparedOnly:mount.querySelector("#attendanceUnprepared").checked,offset:0};chosen.clear();all=false;draftId=null;perform(load);};
   mount.querySelector("#attendanceAll").onchange=e=>{all=e.target.checked;chosen.clear();draftId=null;render();};
   mount.querySelectorAll("[data-attendance-person]").forEach(e=>e.onchange=()=>{draftId=null;e.checked?chosen.add(e.dataset.attendancePerson):chosen.delete(e.dataset.attendancePerson);});
   mount.querySelector("#attendancePrevious").onclick=()=>perform(async()=>{filter.offset=Math.max(0,filter.offset-50);await load();});
   mount.querySelector("#attendanceNext").onclick=()=>perform(async()=>{filter.offset+=50;await load();});
   mount.querySelector("#attendanceTarget").onchange=e=>target=e.target.value;
   mount.querySelector("#attendanceReason").oninput=e=>reason=e.target.value;
   mount.querySelector("#attendanceRevise").onchange=e=>{revise=e.target.checked;mount.querySelector("#attendanceReason").required=revise;};
   mount.querySelector("#attendanceForm").onsubmit=e=>{e.preventDefault();perform(async()=>{
    const input=change(),preview=await bridge.call("PreviewAttendanceChange",input);
    const detail=`التاريخ: ${preview.businessDate}\nعدد الأفراد: ${preview.count}\nالحالة الجديدة: ${preview.statusName}\n${preview.sample.map(p=>p.name+" — "+p.serviceNumberA+": "+(p.statusName||"غير محضر")+" ← "+preview.statusName).join("\n")}${preview.count>preview.sample.length?"\nالمعاينة تعرض أول مائة فرد؛ الاعتماد يشمل كامل العدد أعلاه.":""}\n${input.revise?"سبب التعديل: "+input.reason:""}`;
    if(!await confirm("تأكيد التحضير",detail))return;
    const result=await mutate(input.revise?"ReviseTodayAttendance":"SubmitAttendance",{change:input,previewHash:preview.previewHash});chosen.clear();all=false;draftId=null;notice=`تم اعتماد تحضير ${result.count} فرداً.`;error=false;await load();
   });};
   mount.querySelector("#attendanceSaveDraft").onclick=()=>perform(async()=>{const input=change();if(!await confirm("حفظ مسودة","ستُحفظ اختياراتك دون اعتماد أي تحضير رسمي."))return;await mutate("SaveAttendanceDraft",{change:input});feedback("تم حفظ المسودة دون اعتماد التحضير.");});
   mount.querySelector("#attendanceDrafts").onclick=()=>perform(showDrafts);
  }
  async function showDrafts(){
   const drafts=await bridge.call("ListAttendanceDrafts");const list=mount.querySelector("#attendanceDraftList");
   list.innerHTML=`<h2>المسودات</h2>${drafts.map(d=>`<div class="registry-section"><span>${esc(d.businessDate)} — ${d.personIds.length} فرداً</span> <button class="btn btn-outline" data-load-draft="${esc(d.draftId)}"${d.businessDate!==session.businessDate?" disabled":""}>تحميل ومعاينة</button> <button class="btn btn-outline" data-delete-draft="${esc(d.draftId)}">حذف المسودة</button></div>`).join("")||"<p>لا توجد مسودات محفوظة.</p>"}`;
   list.querySelectorAll("[data-load-draft]").forEach(e=>e.onclick=()=>perform(async()=>{const d=drafts.find(x=>x.draftId===e.dataset.loadDraft);draftId=d.draftId;target=d.statusId;chosen=new Set(d.personIds);all=false;revise=false;reason="";filter={...filter,search:"",siteId:null,statusId:null,unpreparedOnly:false,offset:0};await load();}));
   list.querySelectorAll("[data-delete-draft]").forEach(e=>e.onclick=()=>perform(async()=>{if(!await confirm("حذف مسودة","حذف هذه المسودة فقط؟"))return;await mutate("DeleteAttendanceDraft",{draftId:e.dataset.deleteDraft});feedback("تم حذف المسودة.");await showDrafts();}));
  }
  async function load(){
   const current=++sequence;const nextSession=await bridge.session();
   const result=await Promise.all([bridge.call("QueryDailyRoster",filter),bridge.call("GetDashboardSummary",filter),bridge.call("QuerySites",{includeInactive:false}),bridge.call("GetActiveAttendanceStatuses",{})]);
   if(current!==sequence||!mount.isConnected)return;
   if(session&&session.businessDate!==nextSession.businessDate){chosen.clear();all=false;draftId=null;notice="تغير يوم العمل؛ أعد اختيار الأفراد والمعاينة.";}
   session=nextSession;[rows,summary,sites,statuses]=result;render();if(draftsOnly)await showDrafts();
  }
  const invalidated=()=>{if(!mount.isConnected){window.removeEventListener("altamam:invalidate",invalidated);return;}if(!busy)perform(load);};window.addEventListener("altamam:invalidate",invalidated);
  try{session=await bridge.session();const actor=document.getElementById("attendanceActor");if(actor)actor.textContent=session.account.username+" — "+({1:"الإدارة العامة",2:"الإدارة العادية",3:"مشرف"}[session.account.role]);await load();}catch(e){mount.textContent=e.message;}
 }
 window.addEventListener("DOMContentLoaded",start);window.addEventListener("altamam:attendance-mount",start);
})();
