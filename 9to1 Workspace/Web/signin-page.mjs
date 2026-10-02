import {mountOidcSignIn} from './web-oidc-signin.mjs';
const form=document.getElementById('cake-signin'),button=document.getElementById('cake-signin-button'),status=document.getElementById('cake-signin-status');
let config=null;try{config=JSON.parse(document.getElementById('cake-oidc-host-config').textContent);}catch{}
if(form&&button&&status){const mounted=mountOidcSignIn(form,button,result=>{status.textContent=result.kind==='browser-authorization-started'?'Opening sign-in…':'Sign-in is currently unavailable. Try again when the service is ready.';},window.fetch.bind(window),url=>window.location.assign(url),config?.approved??null,config?.csrf??null);window.addEventListener('pagehide',()=>mounted.dispose(),{once:true});}
