export function applyLiveWork(ledger,observations){
 const updated=structuredClone(ledger);
 for(const part of updated.parts){
  const baseline=part.baseline_development??part.development;part.baseline_development={...baseline};
  const work=observations.filter(item=>item.part===part.part);
  const liveTotal=work.reduce((sum,item)=>sum+item.milestones.length,0),liveDone=work.reduce((sum,item)=>sum+item.milestones.filter(m=>m.done).length,0);
  part.development={completed:baseline.completed+liveDone,total:baseline.total+liveTotal};
  part.scope=[...(part.scope??[]).filter(item=>!item.live_work),...work.map(item=>({app:item.title,platform:'current work',stage:'Development',assessed:true,live_work:true,milestones:item.milestones,acceptance_verified:false,note:item.note}))];
  part.live_work=work;part.note=(part.note??'').split(' Live work:')[0]+(work.length?' Live work: scoped implementation/regression/build/test milestones are refreshed from actual source and result evidence.':'');
 }
 updated.updated_at=new Date().toISOString();updated.live_updated_at=updated.updated_at;return updated;
}
