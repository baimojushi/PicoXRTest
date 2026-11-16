(function(root){
'use strict';
function validSelection(m){return m && m.type==='molink.artwork.selected' && ['exhibition_id','hotspot_id','artwork_id'].every(k=>typeof m[k]==='string' && m[k].length>0 && m[k].length<=128 && !/[\u0000-\u001f]/.test(m[k]));}
function destination(config,m){
  if(!validSelection(m))throw new Error('Invalid artwork selection');
  const base=new URL(config.businessOrigin);
  const local=['localhost','127.0.0.1','[::1]'].includes(base.hostname);
  if(base.username||base.password||base.pathname!=='/'||base.search||base.hash||(base.protocol!=='https:'&&!(config.allowInsecureLocalhost&&local&&base.protocol==='http:')))throw new Error('Invalid business origin');
  const url=new URL('/journey/',base.origin);for(const k of ['exhibition_id','hotspot_id','artwork_id'])url.searchParams.set(k,m[k]);return url;
}
const api={validSelection,destination};
if(typeof module!=='undefined'&&module.exports){module.exports=api;return;}
root.JourneyClient=api;
let config=null,pending=null;
fetch(new URL('journey-client-config.json',location.href),{cache:'no-store'}).then(r=>{if(!r.ok)throw new Error('Configuration unavailable');return r.json();}).then(c=>{config=c;if(pending){const m=pending;pending=null;root.receiveJourneySelection(m);}}).catch(()=>console.warn('[Journey] Configure journey-client-config.json before using the artwork handoff'));
root.receiveJourneySelection=function(m){
  if(!validSelection(m))return;if(!config){pending=m;return;}
  let url;try{url=destination(config,m);}catch{return;}
  root.journeySelectionActive=true;
  if(typeof stopControls==='function')stopControls();
  if(document.pointerLockElement)document.exitPointerLock();
  if(parent!==window){parent.postMessage({type:m.type,exhibition_id:m.exhibition_id,hotspot_id:m.hotspot_id,artwork_id:m.artwork_id},url.origin);return;}
  const panel=document.getElementById('journey-choice'),title=document.getElementById('journey-title'),link=document.getElementById('journey-link');
  title.textContent=String(m.title || '\u4f5c\u54c1\u4ecb\u7ecd').slice(0,200);link.href=url.href;panel.hidden=false;
};
function resume(){root.journeySelectionActive=false;document.getElementById('journey-choice').hidden=true;if(typeof startControlLoop==='function')startControlLoop();}
root.addEventListener('DOMContentLoaded',()=>document.getElementById('journey-close').addEventListener('click',resume));
root.addEventListener('message',e=>{if(!config||e.source!==parent||e.origin!==new URL(config.businessOrigin).origin||e.data?.type!=='molink.gallery.resume')return;resume();});
})(typeof window==='undefined'?globalThis:window);
