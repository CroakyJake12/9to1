const decoder=new TextDecoder();
export function advertisedRef(bytes,branch){
  if(bytes.byteLength>1048576)throw new Error('Git advertisement exceeds bounded size');
  const target='refs/heads/'+branch;let observed=null,offset=0;
  while(offset<bytes.length){
    const head=decoder.decode(bytes.subarray(offset,offset+4));if(!/^[0-9a-f]{4}$/i.test(head))throw new Error('Malformed Git packet length');
    const length=parseInt(head,16);if(length<4){offset+=4;continue;}
    if(offset+length>bytes.length)throw new Error('Truncated Git packet');
    const line=decoder.decode(bytes.subarray(offset+4,offset+length)).split('\0')[0].replace(/\n$/,'');offset+=length;
    const ref=line.match(/^([0-9a-f]{40}) (refs\/heads\/[^\s\0]+)$/);if(!ref||ref[2]!==target)continue;
    if(observed&&observed!==ref[1])throw new Error('Conflicting Git advertisement');observed=ref[1];
  }
  return observed;
}
async function limitedBytes(response){
  const chunks=[];let size=0;for await(const chunk of response.body){size+=chunk.byteLength;if(size>1048576)throw new Error('Git advertisement exceeds bounded size');chunks.push(chunk);}
  const bytes=new Uint8Array(size);let offset=0;for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.byteLength;}return bytes;
}
export async function verifyPublishedSource(source,fetcher=fetch){
  if(source?.repository!=='CroakyJake12/9to1'||!/^[0-9a-f]{40}$/.test(source.sha??'')||typeof source.branch!=='string'||!source.branch||source.branch.length>180)return {verified:false,reason:'Invalid exact source identity'};
  const r=await fetcher(`https://api.github.com/repos/CroakyJake12/9to1/git/ref/heads/${source.branch.split('/').map(encodeURIComponent).join('/')}`,{headers:{Accept:'application/vnd.github+json','User-Agent':'9to1-relay','X-GitHub-Api-Version':'2022-11-28','Cache-Control':'no-cache'},signal:AbortSignal.timeout(15000)});
  const observed=r.ok?(await r.json()).object?.sha:null;
  const result={verified:r.ok&&observed===source.sha,method:'github_rest',http_status:r.status,observed_sha:observed,rate_limit_remaining:r.headers.get('x-ratelimit-remaining'),rate_limit_reset:r.headers.get('x-ratelimit-reset')};
  // A changed/missing/inaccessible ref is never replaced by another source.
  // Only exhausted REST quota uses Git's ordinary public HTTPS ref discovery.
  if(!(r.status===403&&result.rate_limit_remaining==='0')&&r.status!==429)return result;
  await r.body?.cancel();
  const git=await fetcher('https://github.com/CroakyJake12/9to1.git/info/refs?service=git-upload-pack',{headers:{Accept:'application/x-git-upload-pack-advertisement','Git-Protocol':'version=1','User-Agent':'9to1-relay','Cache-Control':'no-cache'},signal:AbortSignal.timeout(15000)});
  if(!git.ok||!git.headers.get('content-type')?.startsWith('application/x-git-upload-pack-advertisement'))return {...result,git_http_status:git.status,reason:'Git ref discovery unavailable'};
  try{const ref=advertisedRef(await limitedBytes(git),source.branch);return {...result,verified:ref===source.sha,method:'git_smart_http',git_http_status:git.status,observed_sha:ref};}
  catch(e){return {...result,verified:false,git_http_status:git.status,reason:e.message};}
}
