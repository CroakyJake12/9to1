import {createServer} from 'node:http';
import {fetchRequest} from '../src/worker.mjs';
for(const [port,brand] of [[4173,'cake'],[4174,'nine']]){
createServer(async(req,res)=>{try{const r=await fetchRequest(new Request('http://127.0.0.1:'+port+req.url,{method:req.method,headers:req.headers}),{LOCAL_DEV:'true',LOCAL_SITE:brand});res.writeHead(r.status,Object.fromEntries(r.headers));res.end(Buffer.from(await r.arrayBuffer()));}catch{res.writeHead(500);res.end('Local server error');}}).listen(port,'127.0.0.1',()=>console.log(brand+' http://127.0.0.1:'+port));
}
