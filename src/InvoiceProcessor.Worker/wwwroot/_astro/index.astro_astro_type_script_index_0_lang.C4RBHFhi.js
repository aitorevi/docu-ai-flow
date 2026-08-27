function s(e,t){window.dispatchEvent(new CustomEvent("app:log",{detail:{msg:e,cls:t}}))}function d(e){return String(e??"").replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/"/g,"&quot;")}function o(e,t,r=!1){const a=document.getElementById(e);a&&(a.textContent=t,a.classList.toggle("is-idle",r))}const u='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',v='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>';function m(e,t){const r=Math.min(e.unmodifiedCount,t),a=t>0?Math.round(r/t*100):0,n=e.isTrusted?`<span class="pill good">${u} Automático</span>`:`<span class="pill review">${v} En revisión</span>`,p=e.isTrusted?`<div class="meter"><div class="meter-track"><div class="meter-fill full" style="width:100%"></div></div>
           <span class="meter-count">${t} / ${t}</span></div>`:`<div class="meter"><div class="meter-track"><div class="meter-fill" style="width:${a}%"></div></div>
           <span class="meter-count">${r} / ${t}</span></div>`;return`
      <tr>
        <td>
          <div class="supplier-name">${d(e.name)}</div>
          ${e.taxId?`<div class="supplier-tax">${d(e.taxId)}</div>`:""}
        </td>
        <td>${p}</td>
        <td>${n}</td>
        <td class="num">${e.pendingCount||"—"}</td>
        <td class="num">${e.archivedCount||"—"}</td>
      </tr>`}function g(e){const t=document.getElementById("trust-root"),r=document.getElementById("trust-hint");if(t){if(r&&(r.textContent=`Automático tras ${e.trustThreshold} confirmaciones seguidas sin correcciones`),e.suppliers.length===0){t.innerHTML=`<p class="empty">Todavía no ha pasado ninguna factura.<br>
        Copia <code class="inline-code">data/samples/*.pdf</code> a <code class="inline-code">data/inbox/</code> para probarlo.</p>`;return}t.innerHTML=`
      <table class="trust-table">
        <thead>
          <tr>
            <th>Proveedor</th>
            <th>Confianza</th>
            <th>Estado</th>
            <th class="num">Pendientes</th>
            <th class="num">Archivadas</th>
          </tr>
        </thead>
        <tbody>${e.suppliers.map(a=>m(a,e.trustThreshold)).join("")}</tbody>
      </table>`}}async function c(){try{const e=await fetch("/api/dashboard");if(!e.ok)throw new Error(`HTTP ${e.status}`);const t=await e.json();o("kpi-pending",String(t.pendingReview),t.pendingReview===0),o("kpi-pending-note",t.pendingReview===0?"todo al día":"esperan una persona"),o("kpi-archived",String(t.archivedInvoices),t.archivedInvoices===0),o("kpi-total",t.totalAmount.toLocaleString("es-ES",{style:"currency",currency:t.currency||"EUR"}),t.archivedInvoices===0),o("kpi-total-note","suma de las archivadas");const r=t.suppliers.length;o("kpi-trusted",`${t.trustedSuppliers}`,t.trustedSuppliers===0),o("kpi-trusted-note",r===0?"sin proveedores aún":`de ${r} conocido(s)`),g(t)}catch(e){const t=document.getElementById("trust-root");t&&(t.innerHTML=`<p class="empty">No se pudo cargar el panel: ${d(e instanceof Error?e.message:String(e))}</p>`)}}function i(e){return document.getElementById(e)?.value??""}async function l(e,t,r){const a=t.textContent;t.disabled=!0,t.textContent=r;try{const n=await fetch(e,{method:"POST"});if(!n.ok)throw new Error(`HTTP ${n.status}`);return await n.json()}finally{t.disabled=!1,t.textContent=a}}document.addEventListener("astro:page-load",()=>{if(!document.getElementById("trust-root"))return;c();const e=document.getElementById("btn-export"),t=document.getElementById("btn-send");e?.addEventListener("click",async()=>{const r=i("export-year"),a=i("export-quarter");try{const n=await l(`/api/export/${r}/${a}`,e,"Generando…");s(n.nothingNew?`Nada nuevo que exportar para ${r} T${a}.`:`${n.exported} factura(s) exportadas → ${n.filePath}`,n.nothingNew?"info":"ok"),window.dispatchEvent(new CustomEvent("app:data-changed")),c()}catch(n){s(`Error al exportar: ${n instanceof Error?n.message:String(n)}`,"err")}}),t?.addEventListener("click",async()=>{const r=i("send-year"),a=i("send-quarter");try{const n=await l(`/api/send/${r}/${a}`,t,"Enviando…");s(n.nothingNew?`Nada nuevo que enviar para ${n.quarter}.`:`${n.sent} factura(s) enviadas al asesor (${n.parts} envío(s)).`,n.nothingNew?"info":"ok"),window.dispatchEvent(new CustomEvent("app:data-changed"))}catch(n){s(`Error al enviar: ${n instanceof Error?n.message:String(n)}`,"err")}})});
