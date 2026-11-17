import {renderCake,navigation as cakeNav} from './cake.mjs';
import {renderNine,navigation as nineNav} from './nine.mjs';
import {shell} from './shell.mjs';
import {privacyPage,termsPage,cookiesPage} from './legal.mjs';
import {filesPrivacyPage} from './files-privacy.mjs';
import {accountPage} from './account.mjs';
import {authRoute} from './auth.mjs';
import {json} from './security.mjs';
import {styles,script} from './site-assets.mjs';
import {NINE_LOGO_URL,CAKEMODS_LOGO_URL,brandAssetResponse} from './brand-assets.mjs';
export const VERSION='2026-11-17.1-preview-design';
function site(request,env){
 const url=new URL(request.url),host=url.hostname;
 if(['cakemods.com','www.cakemods.com'].includes(host))return {brand:'cake',origin:'https://cakemods.com'};
 if(['9to1.uk','www.9to1.uk'].includes(host))return {brand:'nine',origin:'https://9to1.uk'};
 if(host==='cake-public-websites.jcbailey008.workers.dev')return {brand: url.searchParams.get('site')==='nine'?'nine':'cake',origin:'https://cakemods.com',preview:true};
 if(env.LOCAL_DEV==='true'&&['localhost','127.0.0.1'].includes(host))return {brand:env.LOCAL_SITE==='nine'?'nine':'cake',origin:url.origin,preview:true};
 return null;
}
function secure(response){const headers=new Headers(response.headers);headers.set('x-content-type-options','nosniff');headers.set('referrer-policy','strict-origin-when-cross-origin');headers.set('x-frame-options','DENY');headers.set('permissions-policy','camera=(), microphone=(), geolocation=()');headers.set('content-security-policy',"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'");headers.set('strict-transport-security','max-age=31536000');headers.set('x-cake-version',VERSION);return new Response(response.body,{status:response.status,headers});}
export async function fetchRequest(request,env){
 const selected=site(request,env);if(!selected)return secure(new Response('Unknown site',{status:421}));
 const {brand,origin,preview}=selected,url=new URL(request.url);let path=url.pathname;
 if(!preview&&url.origin!==origin)return secure(new Response(null,{status:308,headers:{location:origin+path+url.search}}));
 try{
  const asset=brandAssetResponse(request);if(asset)return secure(asset);
  if(path==='/health')return secure(json({status:'ok',version:VERSION,site:brand}));
  if(preview&&(path.startsWith('/auth/')||path==='/oauth/callback'))return secure(new Response(null,{status:303,headers:{location:(brand==='nine'?'https://9to1.uk':'https://cakemods.com')+'/account'}}));
  const auth=await authRoute(request,env,origin,brand);if(auth)return secure(auth);
  if(brand==='nine'&&!preview&&env.FILES&&(path==='/files'||path.startsWith('/files/')||path.startsWith('/api/files/v1/')))return env.FILES.fetch(request);
  if(!['GET','HEAD'].includes(request.method))return secure(json({error:'method_not_allowed'},405));
  if(path==='/assets/site.css')return secure(new Response(request.method==='HEAD'?null:styles,{headers:{'content-type':'text/css; charset=utf-8','cache-control':'public,max-age=300'}}));
  if(path==='/assets/site.js')return secure(new Response(request.method==='HEAD'?null:script,{headers:{'content-type':'text/javascript; charset=utf-8','cache-control':'public,max-age=300'}}));
  if(path==='/favicon.svg')return secure(new Response(null,{status:302,headers:{location:brand==='cake'?CAKEMODS_LOGO_URL:NINE_LOGO_URL,'cache-control':'public,max-age=300'}}));
  if(path==='/robots.txt')return secure(new Response(preview?'User-agent: *\nDisallow: /':'User-agent: *\nAllow: /\nDisallow: /account\nDisallow: /auth/\nDisallow: /api/\nSitemap: '+origin+'/sitemap.xml',{headers:{'content-type':'text/plain'}}));
  if(path==='/sitemap.xml'){const paths=brand==='cake'?['/','/products/','/communities/','/privacy/','/terms/','/cookies/']:['/','/apps/','/ai/','/about/','/privacy/','/terms/','/cookies/'];return secure(new Response(`<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${paths.map(p=>`<url><loc>${origin}${p}</loc></url>`).join('')}</urlset>`,{headers:{'content-type':'application/xml'}}));}
  const redirects={'/member-login/':'/auth/sign-in','/member-register/':'/auth/sign-up','/member-dashboard/':'/account','/9to1-studio/':'https://9to1.uk/','/9to1-ai/':'https://9to1.uk/ai/','/ai/':brand==='cake'?'https://9to1.uk/ai/':null,'/roadmap/':'https://trello.com/b/GB0oqo1e/cake-roadmap'};
  if(redirects[path])return secure(new Response(null,{status:302,headers:{location:redirects[path]}}));
  if(!path.endsWith('/')&&path!=='/account')return secure(new Response(null,{status:308,headers:{location:origin+path+'/'}}));
  const page=path==='/account'||path==='/account/'?accountPage(env.ACCOUNTS_AVAILABLE==='invite-only'&&Boolean(brand==='cake'?env.CAKE_CLIENT_ID:env.NINE_CLIENT_ID),env.ACCOUNTS_AVAILABLE==='private-testing'&&Boolean(brand==='cake'?env.CAKE_CLIENT_ID:env.NINE_CLIENT_ID),Boolean(env.FILES)):path==='/privacy/'?privacyPage:path==='/files-privacy/'?filesPrivacyPage:path==='/terms/'?termsPage:path==='/cookies/'?cookiesPage:(brand==='cake'?renderCake(path):renderNine(path));
  const notFound={title:'Page not found',description:'This page is no longer available.',body:'<section class="prose-page"><p class="eyebrow">404 / A DIFFERENT DIRECTION</p><h1>Let’s find your way back.</h1><p>This page has moved or is no longer available.</p><a class="button primary" href="/">Back to home ↗</a></section>'};
  const headers={'content-type':'text/html; charset=utf-8','cache-control':path.startsWith('/account')?'no-store':'public,max-age=60'};
  if(preview||path.startsWith('/account'))headers['x-robots-tag']='noindex';
  return secure(new Response(request.method==='HEAD'?null:shell(page||notFound,brand,brand==='cake'?cakeNav:nineNav,path,origin),{status:page?200:404,headers}));
 }catch{console.error(JSON.stringify({event:'request_failed',site:brand,version:VERSION}));return secure(json({error:'service_unavailable'},503));}
}
export default {fetch:fetchRequest};
