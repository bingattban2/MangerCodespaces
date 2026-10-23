"use strict";
(()=>{window.addEventListener("DOMContentLoaded",async()=>{
 const mount=document.getElementById("exportMount");if(!mount)return;const bridge=window.AlTamamBridge;
 try{await bridge.session();const [periods,sites]=await Promise.all([bridge.call("GetExportablePeriods",{}),bridge.call("QuerySites",{includeInactive:true})]);
  mount.innerHTML='<div class="registry-toolbar"><label>الشهر<select id="exportPeriod" class="form-select"></select></label><label>الموقع<select id="exportSite" class="form-select"><option value="">جميع المواقع — كشف عام وكشف لكل موقع</option></select></label></div><p class="registry-help">الكشف يحفظ أسماء المواقع والحالات بتاريخها، ويحتوي كل أيام الشهر. النصوص والأرقام (ع) تُحفظ كما هي.</p><section id="exportControls"></section>';
  const period=mount.querySelector("#exportPeriod"),site=mount.querySelector("#exportSite");periods.forEach(p=>period.add(new Option(`${p.year} / ${p.month}`,`${p.year}-${p.month}`)));sites.forEach(s=>site.add(new Option(s.displayName+(s.active?"":" (معطل)"),s.siteId)));
  const controls=window.AlTamamExport.mount(mount.querySelector("#exportControls"),()=>{const [year,month]=period.value.split("-").map(Number);return {kind:"monthly",year,month,siteId:site.value||null};});period.onchange=site.onchange=()=>controls.invalidate();window.addEventListener("altamam:invalidate",()=>controls.invalidate());
 }catch(e){mount.textContent=e.message;}
});})();
