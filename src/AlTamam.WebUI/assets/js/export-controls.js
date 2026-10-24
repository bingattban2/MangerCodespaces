"use strict";
(()=>{
 const bridge=window.AlTamamBridge;
 window.AlTamamExport=Object.freeze({mount(container,getQuery){
  container.innerHTML='<div class="registry-actions"><button type="button" class="btn btn-teal" data-export-preview>معاينة التصدير</button><button type="button" class="btn btn-primary" data-export-save disabled>اختيار الملف وحفظه</button><button type="button" class="btn btn-outline" data-export-cancel disabled>إلغاء التصدير</button></div><p data-export-message role="status" aria-live="polite"></p><progress data-export-progress value="0" max="1" aria-label="تقدم التصدير"></progress><ul data-export-files></ul>';
  const previewButton=container.querySelector('[data-export-preview]'),save=container.querySelector('[data-export-save]'),cancel=container.querySelector('[data-export-cancel]'),message=container.querySelector('[data-export-message]'),progress=container.querySelector('progress'),files=container.querySelector('ul');
  let preview=null,query=null,job=null,requestId=null,timer=0,busy=false,closed=false;
  function show(state){job=state;progress.max=Math.max(1,state.totalRows);progress.value=state.writtenRows;cancel.disabled=state.state!=="running";previewButton.disabled=state.state==="running";save.disabled=true;
   message.textContent=state.state==="completed"?`تم حفظ ${state.fileName} بنجاح.`:state.state==="failed"?(state.error||"تعذر حفظ التصدير."):state.state==="cancelled"?"أُلغي التصدير؛ لم يُستبدل الملف المختار.":`جارٍ تجهيز الملف: ${state.writtenRows} من ${state.totalRows} صف.`;
   if(state.state==="running"&&!closed)timer=setTimeout(poll,700);
  }
  async function poll(){if(closed||!job?.jobId)return;try{show(await bridge.call("GetExportProgress",{jobId:job.jobId}));}catch(e){message.textContent=e.message;cancel.disabled=true;previewButton.disabled=false;}}
  previewButton.onclick=async()=>{if(busy)return;busy=true;save.disabled=true;preview=null;requestId=null;files.replaceChildren();previewButton.disabled=true;try{query=getQuery();if(!query)throw new Error("اعرض التقرير المطلوب أولاً.");query=JSON.parse(JSON.stringify(query));preview=await bridge.call("PreviewMonthlyExport",query);for(const file of preview.files){const item=document.createElement("li");item.textContent=file;files.append(item);}message.textContent=`المعاينة: ${preview.rowCount} صف. ${preview.files.length>1?"سيُحفظ ملف مضغوط يحتوي الكشف العام وكشوف المواقع.":"سيُحفظ ملف Excel."}`;save.disabled=false;}catch(e){message.textContent=e.message;}finally{busy=false;previewButton.disabled=false;}};
  save.onclick=async()=>{if(busy||!preview)return;busy=true;save.disabled=true;previewButton.disabled=true;requestId??=crypto.randomUUID();message.textContent="اختر مكان الحفظ في نافذة Windows.";
   try{show(await bridge.call(query.kind==="monthly"?"ExportMonthlyWorkbookSet":"ExportPersonLifetimeExcel",{query,previewHash:preview.previewHash},requestId));}
   catch(e){message.textContent=e.message;save.disabled=e.code!=="Timeout";if(e.code!=="Timeout"){preview=null;requestId=null;}previewButton.disabled=false;}
   finally{busy=false;}
  };
  cancel.onclick=async()=>{if(!job?.jobId)return;cancel.disabled=true;clearTimeout(timer);try{show(await bridge.call("CancelExport",{jobId:job.jobId}));}catch(e){message.textContent=e.message;}};
  window.addEventListener("beforeunload",()=>{closed=true;clearTimeout(timer);},{once:true});
  bridge.call("GetExportProgress",{jobId:""}).then(state=>{if(!preview&&!busy&&(state.state==="running"||document.body.dataset.routeKey==="ExportProgressAndResult"))show(state);}).catch(()=>{});
  return {invalidate(){preview=null;query=null;requestId=null;save.disabled=true;files.replaceChildren();}};
 }});
})();
