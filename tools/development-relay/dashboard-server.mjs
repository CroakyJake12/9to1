import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {randomUUID,timingSafeEqual} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {workerView,queueSteering} from './worker-view.mjs';
import {listReviews,submitAppFeedback,submitWorkFeedback,reviewImage,feedbackDelivery} from './reviews.mjs';
import {augmentDashboardStatus} from './dashboard-data.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));
const assets=new Map([['/dashboard.css',['dashboard.css','text/css']],['/dashboard.mjs',['dashboard.mjs','text/javascript']],['/dashboard-model.mjs',['dashboard-model.mjs','text/javascript']]]);
const actions=new Set(['home','away','pause','resume','checkpoint','handover','recheck']);
export async function startDashboard({port=8795,backendPort=8794,privateRoot=null}={}){
 if(!Number.isSafeInteger(port)||!Number.isSafeInteger(backendPort)||port===backendPort||port<1024||port>65535||backendPort<1024||backendPort>65535)throw new Error('Distinct loopback ports required');
 const origin=`http://127.0.0.1:${port}`,backend=`http://127.0.0.1:${backendPort}`,csrf=randomUUID();
 const server=createServer(async(req,res)=>{
  const respond=(code,body,type='application/json')=>{res.writeHead(code,{'Content-Type':type,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'"});res.end(type==='application/json'?JSON.stringify(body):body);};
  try{
   if(req.headers.host!==`127.0.0.1:${port}`)return respond(403,{error:'Invalid host'});
   const url=new URL(req.url,origin);
   if(req.method==='GET'&&url.pathname==='/')return respond(200,(await readFile(path.join(here,'dashboard.html'),'utf8')).replace('__CSRF__',csrf),'text/html; charset=utf-8');
   if(req.method==='GET'&&assets.has(req.url)){const [file,type]=assets.get(req.url);return respond(200,await readFile(path.join(here,file),'utf8'),type);}
   if(req.method==='GET'&&privateRoot&&url.pathname==='/worker'&&url.searchParams.size===1)return respond(200,await workerView(privateRoot,url.searchParams.get('name')));
   if(req.method==='GET'&&privateRoot&&req.url==='/reviews')return respond(200,{captures:listReviews(privateRoot).filter(c=>c.app!=='Development relay'),delivery:feedbackDelivery(privateRoot)});
   if(req.method==='GET'&&privateRoot&&url.pathname.startsWith('/review-image/')){const bytes=await reviewImage(privateRoot,url.pathname.slice(14));return respond(200,bytes,bytes.subarray(0,3).toString('hex')==='ffd8ff'?'image/jpeg':'image/png');}
   if(req.method==='POST'&&req.url==='/review'&&privateRoot){const token=req.headers['x-relay-csrf'];if(req.headers.origin!==origin||typeof token!=='string'||Buffer.byteLength(token)!==Buffer.byteLength(csrf)||!timingSafeEqual(Buffer.from(token),Buffer.from(csrf)))return respond(403,{error:'Review must originate in this dashboard'});let raw='';for await(const chunk of req){raw+=chunk;if(Buffer.byteLength(raw)>32768)return respond(413,{error:'Comment too large'});}return respond(200,await submitAppFeedback(privateRoot,JSON.parse(raw)));}
   if(req.method==='POST'&&req.url==='/work-feedback'&&privateRoot){const token=req.headers['x-relay-csrf'];if(req.headers.origin!==origin||typeof token!=='string'||Buffer.byteLength(token)!==Buffer.byteLength(csrf)||!timingSafeEqual(Buffer.from(token),Buffer.from(csrf)))return respond(403,{error:'Feedback must originate in this dashboard'});let raw='';for await(const chunk of req){raw+=chunk;if(Buffer.byteLength(raw)>32768)return respond(413,{error:'Feedback too large'});}return respond(200,await submitWorkFeedback(privateRoot,JSON.parse(raw)));}
   if(req.method==='POST'&&req.url==='/steer'&&privateRoot){const token=req.headers['x-relay-csrf'];if(req.headers.origin!==origin||typeof token!=='string'||Buffer.byteLength(token)!==Buffer.byteLength(csrf)||!timingSafeEqual(Buffer.from(token),Buffer.from(csrf)))return respond(403,{error:'Steering must originate in this dashboard'});let raw='';for await(const chunk of req){raw+=chunk;if(Buffer.byteLength(raw)>32768)return respond(413,{error:'Steering too large'});}return respond(200,await queueSteering(privateRoot,JSON.parse(raw)));}
   if(req.method==='GET'&&(req.url==='/status'||url.pathname==='/diagnostics'&&url.searchParams.size===1&&url.searchParams.has('task'))){const upstream=await fetch(backend+req.url,{signal:AbortSignal.timeout(10000)}),data=await upstream.json();if(req.url==='/status'&&privateRoot)await augmentDashboardStatus(data,privateRoot);return respond(upstream.status,data);}
   if(req.method==='POST'&&url.pathname.startsWith('/control/')&&actions.has(url.pathname.slice(9))&&!url.search){
    const token=req.headers['x-relay-csrf'];if(req.headers.origin!==origin||typeof token!=='string'||Buffer.byteLength(token)!==Buffer.byteLength(csrf)||!timingSafeEqual(Buffer.from(token),Buffer.from(csrf)))return respond(403,{error:'Control must originate in this dashboard'});
    if(req.headers['transfer-encoding']||Number(req.headers['content-length']??0)>0)return respond(400,{error:'Controls do not accept a request body'});
    const html=await(await fetch(backend,{signal:AbortSignal.timeout(5000)})).text();const upstreamToken=html.match(/const csrf="([^"]+)"/)?.[1]??html.match(/name="relay-csrf" content="([^"]+)"/)?.[1];if(!upstreamToken)throw new Error('Backend control identity unavailable');
    const upstream=await fetch(backend+url.pathname,{method:'POST',headers:{origin:backend,'x-relay-csrf':upstreamToken},signal:AbortSignal.timeout(30000)});return respond(upstream.status,await upstream.json());
   }
   return respond(404,{error:'Unknown route'});
  }catch(e){respond(502,{error:'Relay backend unavailable: '+e.message});}
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});return {server,url:origin,stop:()=>new Promise(r=>server.close(r))};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){const app=await startDashboard({port:Number(process.argv[2]??8795),backendPort:Number(process.argv[3]??8794),privateRoot:process.argv[4]?path.resolve(process.argv[4]):null});console.log(app.url);}


