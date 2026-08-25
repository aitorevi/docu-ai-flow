function y(e){const n=document.getElementById("confirm-modal"),a=document.getElementById("confirm-title"),s=document.getElementById("confirm-message"),d=n?.querySelector(".confirm-ok"),l=n?.querySelector(".confirm-cancel"),v=n?.querySelector("[data-close]");return!n||!a||!s||!d||!l||!v?Promise.resolve(window.confirm(e.message)):(a.textContent=e.title,s.textContent=e.message,d.textContent=e.confirmLabel,d.classList.toggle("is-primary",e.variant==="primary"),new Promise(r=>{const t=u=>{n.hidden=!0,d.removeEventListener("click",i),l.removeEventListener("click",o),v.removeEventListener("click",o),document.removeEventListener("keydown",c),r(u)},i=()=>t(!0),o=()=>t(!1),c=u=>{u.key==="Escape"&&o()};d.addEventListener("click",i),l.addEventListener("click",o),v.addEventListener("click",o),document.addEventListener("keydown",c),n.hidden=!1,l.focus()}))}function A(e,n,a){e.innerHTML=`
    <div class="load-error">
      <span class="load-error-msg">${C(n)}</span>
      <button type="button" class="load-error-retry">Reintentar</button>
    </div>`,e.querySelector(".load-error-retry")?.addEventListener("click",()=>a(),{once:!0})}function C(e){return e.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;")}const N=.6,L=typeof navigator<"u"&&/Mac|iPhone|iPad|iPod/i.test(navigator.userAgent),w=`<div class="confirm-hint">${L?"<kbd>⌘</kbd> <kbd>Enter</kbd>":"<kbd>Ctrl</kbd> <kbd>Enter</kbd>"} para confirmar</div>`;function T(e){if(e.key!=="Enter"||!(e.ctrlKey||e.metaKey))return;const n=document.getElementById("review-form");if(!n)return;const a=n.querySelector(".btn-confirm");!a||a.disabled||(e.preventDefault(),n.requestSubmit(a))}document.addEventListener("keydown",T);function g(e,n){window.dispatchEvent(new CustomEvent("app:log",{detail:{msg:e,cls:n}}))}function f(e){return String(e??"").replace(/"/g,"&quot;").replace(/</g,"&lt;")}function E(e){return!e||e.startsWith("0001-")?"":e}function F(e,n){const a=e.confidence[n];return a&&a.confidence<N?" low-conf":""}function S(e,n){const a=e.confidence[n];return a?`<span class="conf">${Math.round(a.confidence*100)}%</span>`:""}function D(e,n){const a=e.confidence[n];return!a||a.confidence===0}function h(e,n,a,s,d="text",l=!1){return l&&D(e,n)?`
        <div class="form-row not-captured">
          <label>${a} <span class="conf-na">no capturado</span></label>
          <input data-field="${n}" type="${d}" value="${f(s)}" disabled />
        </div>`:`
      <div class="form-row${F(e,n)}">
        <label>${a} ${S(e,n)}</label>
        <input data-field="${n}" type="${d}" value="${f(s)}" />
      </div>`}function $(e,n,a="text",s=""){const d=s?` placeholder="${f(s)}"`:"";return`
      <div class="form-row">
        <label>${e}</label>
        <input data-field="${n}" type="${a}"${a==="number"?' step="0.01" min="0"':""}${d} value="" />
      </div>`}function k(e,n){return e.length<2?"":`
      <div class="pending-picker">
        <span class="pending-picker-label">Facturas pendientes</span>
        <div class="pending-list">${e.map(s=>{const l=s.contentHash===n?" active":"";if(s.requiresManualEntry){const v=s.supplierName?f(s.supplierName):"PDF sin texto extraíble";return`
          <button type="button" class="pending-pick${l}" data-hash="${f(s.contentHash)}">
            <span class="pending-pick-name">${v}</span>
            <span class="pending-pick-meta"><span class="manual-tag">Alta manual</span></span>
          </button>`}return`
        <button type="button" class="pending-pick${l}" data-hash="${f(s.contentHash)}">
          <span class="pending-pick-name">${f(s.supplierName)}${s.sourcedFromOcr?' <span class="ocr-tag">OCR</span>':""}${s.totalAmount<0?' <span class="abono-tag">ABONO</span>':""}</span>
          <span class="pending-pick-meta">Nº ${f(s.invoiceNumber)} · ${s.totalAmount.toFixed(2)} ${f(s.currency)}</span>
        </button>`}).join("")}</div>
      </div>`}function H(e,n){const a=e.trust.unmodifiedCount>=e.trust.threshold;return`
        <form class="form-pane" id="review-form" data-hash="${e.contentHash}" data-currency="${f(e.currency)}" data-supplier-name="${f(e.supplierName)}" data-supplier-tax-id="${f(e.supplierTaxId??"")}">
          ${e.sourcedFromOcr?'<div class="ocr-banner">📷 Capturada por <strong>OCR</strong> (factura escaneada) — revisa los datos con atención, el OCR puede leer mal algún dígito.</div>':""}
          ${e.netAmount<0?'<div class="abono-banner">🔁 <strong>Abono / rectificativa</strong> (importes en negativo) — verifica en el PDF que de verdad es un abono antes de confirmar.</div>':""}
          <div class="supplier-header">
            <div class="supplier-line">
              <strong>${f(e.supplierName)}</strong>
              ${e.supplierTaxId?`<span>${f(e.supplierTaxId)}</span>`:""}
            </div>
            <span class="trust-badge${a?" trusted":""}" title="Al llegar a ${e.trust.threshold} confirmaciones sin correcciones, las facturas de este proveedor se archivan automáticamente.">
              Confianza: ${e.trust.unmodifiedCount} de ${e.trust.threshold} confirmaciones
            </span>
          </div>

          <div class="form-group">
            <span class="form-group-label">Documento</span>
            ${h(e,"invoiceNumber","Nº factura",e.invoiceNumber)}
            <div class="form-dates">
              ${h(e,"issueDate","Fecha emisión",E(e.issueDate),"date")}
              ${h(e,"dueDate","Fecha vencimiento",E(e.dueDate),"date")}
            </div>
          </div>

          <div class="form-group">
            <span class="form-group-label">Importes</span>
            <div class="amounts">
              ${h(e,"netAmount","Base",String(e.netAmount),"number")}
              ${h(e,"taxAmount","IVA",String(e.taxAmount),"number",!0)}
              ${h(e,"totalAmount","Total",String(e.totalAmount),"number",!0)}
            </div>
          </div>

          <div class="actions">
            <button type="submit" class="btn-confirm">Confirmar</button>
            <button type="button" class="btn-requeue">Reprocesar</button>
            <button type="button" class="btn-reject">Rechazar</button>
          </div>
          ${w}
          ${k(n,e.contentHash)}
        </form>`}function I(e,n){return`
        <form class="form-pane" id="review-form"
              data-hash="${f(e.contentHash)}"
              data-currency="${f(e.currency)}"
              data-manual-entry="true">
          <div class="manual-entry-banner">
            Alta manual — el extractor no pudo leer este PDF. Introduce los datos a mano.
          </div>
          <div class="supplier-header">
            <div class="supplier-line">
              <strong class="unidentified">Proveedor sin identificar</strong>
            </div>
          </div>

          <div class="form-group">
            <span class="form-group-label">Proveedor</span>
            <div class="form-row">
              <label>Nombre</label>
              <input data-field="supplierName" type="text"
                     placeholder="Nombre del proveedor" autocomplete="off" value="" />
            </div>
            <div class="form-row">
              <label>NIF/CIF</label>
              <input data-field="supplierTaxId" type="text" placeholder="A12345678" value="" />
            </div>
          </div>

          <div class="form-group">
            <span class="form-group-label">Documento</span>
            ${$("Nº factura","invoiceNumber","text","FAC-2025-001")}
            <div class="form-dates">
              ${$("Fecha emisión","issueDate","date")}
              ${$("Fecha vencimiento","dueDate","date")}
            </div>
          </div>

          <div class="form-group">
            <span class="form-group-label">Importes</span>
            <div class="amounts">
              ${$("Base","netAmount","number","0.00")}
              ${$("IVA","taxAmount","number","0.00")}
              ${$("Total","totalAmount","number","0.00")}
            </div>
          </div>

          <div class="manual-entry-error" style="display:none;"></div>

          <div class="actions">
            <button type="submit" class="btn-confirm">Confirmar</button>
            <button type="button" class="btn-requeue">Reprocesar</button>
            <button type="button" class="btn-reject">Rechazar</button>
          </div>
          ${w}
          ${k(n,e.contentHash)}
        </form>`}function P(e){const n=m(e,"supplierName"),a=m(e,"supplierTaxId"),s=m(e,"invoiceNumber"),d=m(e,"issueDate"),l=m(e,"dueDate"),v=Number(m(e,"netAmount")||"0"),r=Number(m(e,"taxAmount")||"0"),t=Number(m(e,"totalAmount")||"0");if(!n)return"El nombre del proveedor es obligatorio.";if(!a)return"El NIF/CIF del proveedor es obligatorio.";if(!s)return"El número de factura es obligatorio.";if(!d)return"La fecha de emisión es obligatoria.";const i=d.split("-")[0]??"0";return parseInt(i,10)<2e3?"El año de la fecha de emisión debe ser 2000 o posterior.":t===0?"El importe total no puede ser cero.":Math.abs(v+r-t)>.05?`Los importes no son coherentes: ${v.toFixed(2)} + ${r.toFixed(2)} ≠ ${t.toFixed(2)}.`:l&&l<d?"La fecha de vencimiento no puede ser anterior a la de emisión.":null}function m(e,n){return(e.querySelector(`[data-field="${n}"]`)?.value??"").trim()}document.addEventListener("astro:page-load",()=>{const e=document.getElementById("review-root"),n=document.getElementById("queue-count");if(!e)return;let a=[];function s(r){let t=r.querySelector(".split");return t||(r.innerHTML=`
          <div class="split">
            <div class="pdf-pane">
              <iframe title="PDF de la factura"></iframe>
              <div class="pdf-loading">Cargando PDF…</div>
              <a class="pdf-open" target="_blank" rel="noopener">Abrir en pestaña nueva ↗</a>
            </div>
          </div>`,t=r.querySelector(".split")),t}function d(r,t){const i=r.querySelector("iframe"),o=r.querySelector(".pdf-open"),c=r.querySelector(".pdf-loading"),u=`/api/pending/${t}/pdf`;o&&(o.href=u),!(!i||i.getAttribute("src")===u)&&(c&&(c.hidden=!1,i.addEventListener("load",()=>{c.hidden=!0},{once:!0})),i.src=u)}function l(r){if(!e)return;const t=r.requiresManualEntry?I(r,a):H(r,a),i=s(e);d(i,r.contentHash),i.querySelector(".form-pane")?.remove(),i.insertAdjacentHTML("beforeend",t)}async function v(){if(e)try{const r=await fetch("/api/pending");if(!r.ok)throw new Error(`HTTP ${r.status}`);const t=await r.json();if(!Array.isArray(t))throw new Error("Respuesta inesperada del servidor");if(a=t,n&&(n.textContent=`${t.length} pendiente(s)`),t.length===0){e.innerHTML='<p class="info">No hay facturas pendientes de revisión.</p>';return}const i=t[0];if(!i)return;const o=await fetch(`/api/pending/${i.contentHash}`);if(!o.ok)throw new Error(`HTTP ${o.status}`);l(await o.json())}catch(r){const t=r instanceof Error?r.message:String(r);e&&A(e,`Error al cargar: ${t}`,()=>{v()})}}e.addEventListener("submit",async r=>{r.preventDefault();const t=r.target;if(t.id!=="review-form")return;const i=t.dataset.hash??"",o=t.dataset.manualEntry==="true";if(o){const p=P(t),b=t.querySelector(".manual-entry-error");if(p){b&&(b.textContent=p,b.style.display="block");return}b&&(b.style.display="none")}const c=t.querySelector(".btn-confirm");c&&(c.disabled=!0,c.textContent="Guardando...");const u={invoiceNumber:m(t,"invoiceNumber"),supplierName:o?m(t,"supplierName"):t.dataset.supplierName??"",supplierTaxId:o?m(t,"supplierTaxId")||null:t.dataset.supplierTaxId||null,issueDate:m(t,"issueDate"),dueDate:m(t,"dueDate")||null,netAmount:Number(m(t,"netAmount")),taxAmount:Number(m(t,"taxAmount")),totalAmount:Number(m(t,"totalAmount")),currency:t.dataset.currency??"EUR"};try{const p=await fetch(`/api/pending/${i}`,{method:"PUT",headers:{"Content-Type":"application/json"},body:JSON.stringify(u)});if(!p.ok){const x=await p.json().catch(()=>({error:`HTTP ${p.status}`}));throw new Error(x?.error??`HTTP ${p.status}`)}const b=await p.json();g(b.isTrusted?`Factura confirmada. El proveedor alcanzó ${b.unmodifiedCount} sin cambios y pasa a automático.`:b.wasModified?"Factura confirmada con correcciones (contador del proveedor reiniciado).":`Factura confirmada sin cambios (${b.unmodifiedCount} consecutivas).`,"ok"),window.dispatchEvent(new CustomEvent("app:data-changed")),await v()}catch(p){const b=p instanceof Error?p.message:String(p);g(`Error al confirmar: ${b}`,"err"),c&&(c.disabled=!1,c.textContent="Confirmar")}}),e.addEventListener("click",async r=>{const t=r.target;if(!t.classList.contains("btn-reject"))return;const o=t.closest("#review-form")?.dataset.hash??"";if(!o||!await y({title:"Rechazar factura",message:"El PDF se moverá a la carpeta de fallidas.",confirmLabel:"Rechazar"}))return;const c=t;c.disabled=!0,c.textContent="...";try{const u=await fetch(`/api/pending/${o}`,{method:"DELETE"});if(!u.ok)throw new Error(`HTTP ${u.status}`);g("Factura rechazada (movida a fallidas).","ok"),window.dispatchEvent(new CustomEvent("app:data-changed")),await v()}catch(u){const p=u instanceof Error?u.message:String(u);g(`Error al rechazar: ${p}`,"err"),c.disabled=!1,c.textContent="Rechazar"}}),e.addEventListener("click",async r=>{const t=r.target;if(!t.classList.contains("btn-requeue"))return;const o=t.closest("#review-form")?.dataset.hash??"";if(!o||!await y({title:"Reprocesar factura",message:"Volverá a data/inbox/ y se procesará de nuevo (p. ej. tras crear su plantilla).",confirmLabel:"Reprocesar",variant:"primary"}))return;const c=t;c.disabled=!0,c.textContent="...";try{const u=await fetch(`/api/pending/${o}/requeue`,{method:"POST"});if(!u.ok)throw new Error(`HTTP ${u.status}`);g("Factura devuelta al inbox para reprocesar.","ok"),window.dispatchEvent(new CustomEvent("app:data-changed")),await v()}catch(u){const p=u instanceof Error?u.message:String(u);g(`Error al reprocesar: ${p}`,"err"),c.disabled=!1,c.textContent="Reprocesar"}}),e.addEventListener("click",async r=>{const t=r.target.closest(".pending-pick");if(!t||t.classList.contains("active")||!e)return;const i=t.dataset.hash??"";if(i)try{const o=await fetch(`/api/pending/${i}`);if(!o.ok)throw new Error(`HTTP ${o.status}`);l(await o.json())}catch(o){g(`Error al cargar la factura: ${o instanceof Error?o.message:String(o)}`,"err")}}),v()});
