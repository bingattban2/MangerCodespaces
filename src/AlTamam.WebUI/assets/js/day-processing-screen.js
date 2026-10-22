"use strict";
(()=>{
 const bridge=window.AlTamamBridge;
 window.addEventListener("DOMContentLoaded",async()=>{
  const form=document.getElementById("dayMonitorForm");if(!form)return;
  const date=document.getElementById("dayMonitorDate"),output=document.getElementById("dayMonitorResult");
  let busy=false;
  async function refresh(){
   if(busy)return;busy=true;form.querySelector("button").disabled=true;output.setAttribute("aria-busy","true");
   try{
    const state=await bridge.call("GetAutoAttendanceRun",{businessDate:date.value});
    output.replaceChildren();
    const lines=[`يوم العمل: ${state.businessDate}`,`موعد الاستكمال: ${state.dueTimeLocal}`,state.completed?"اكتملت المهمة لهذا اليوم.":"لا يوجد استكمال تلقائي معتمد لهذا اليوم.",state.dayClosed?"اليوم مغلق أمام التحضير العادي.":"اليوم مفتوح للتحضير.",state.monthClosed?"الشهر مغلق.":"",state.run?.summaryArabic,state.run?`وقت الاعتماد: ${state.run.completedAtUtc}`:"",state.run?.catchUp?"نُفذت المهمة بعد فوات موعدها عند تشغيل البرنامج أو استئنافه.":"",state.lastFailure];
    for(const line of lines.filter(Boolean)){const p=document.createElement("p");p.textContent=line;output.append(p);}
   }catch(e){output.textContent=e.message;}finally{busy=false;form.querySelector("button").disabled=false;output.removeAttribute("aria-busy");}
  }
  form.addEventListener("submit",e=>{e.preventDefault();refresh();});
  try{const session=await bridge.session();date.value=session.businessDate;date.max=session.businessDate;await refresh();}catch(e){output.textContent=e.message;}
 });
})();
