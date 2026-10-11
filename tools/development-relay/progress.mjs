const titles=['Core platform & foundations','Full first apps & wider RCVP','Full non-OS ecosystem','9to1OS'];
function ratio(metric){if(!metric||!Number.isSafeInteger(metric.total)||metric.total<=0||!Number.isSafeInteger(metric.completed)||metric.completed<0||metric.completed>metric.total)return null;return 100*metric.completed/metric.total;}
export function deliveryProgress(ledger,tasks=[]){
 return {updated_at:ledger?.updated_at??null,basis:ledger?.basis??'No development ledger recorded',coverage:ledger?.coverage??'unmapped',weights:{development:80,acceptance:20},parts:titles.map((title,index)=>{
  const part=index+1,record=ledger?.parts?.find(p=>p.part===part),unknown=record?.scope?.length>0&&record.scope.every(item=>item.assessed===false),development=unknown?null:ratio(record?.development),acceptance=unknown?null:ratio(record?.acceptance);
  return {part,title,development_percent:development,acceptance_percent:acceptance,total_percent:development===null||acceptance===null?null:Math.round((development*.8+acceptance*.2)*10)/10,assessment:unknown?'UNASSESSED':'TRACKED',development:record?.development??null,acceptance:record?.acceptance??null,note:record?.note??'Scope has not been measured',scope:record?.scope??[],active_tasks:tasks.filter(t=>t.part===part&&t.status==='WORKING').map(t=>({id:t.id,executor:t.executor,owner:t.owner}))};
 })};
}
