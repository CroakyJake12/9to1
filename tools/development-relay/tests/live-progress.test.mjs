import test from 'node:test';
import assert from 'node:assert/strict';
import {applyLiveWork} from '../live-progress.mjs';
import {deliveryProgress} from '../progress.mjs';
test('Live source and verification milestones move development without inflating acceptance or double-counting refreshes',()=>{
 const ledger={parts:[{part:1,development:{completed:45,total:86},acceptance:{completed:0,total:22},scope:[]}]};
 const source={part:1,title:'CUI feature',milestones:[{done:true},{done:true},{done:false},{done:false}]};
 const first=applyLiveWork(ledger,[source]);assert.deepEqual(first.parts[0].development,{completed:47,total:90});assert.equal(deliveryProgress(first).parts[0].total_percent,41.8);
 const repeat=applyLiveWork(first,[source]);assert.deepEqual(repeat.parts[0].development,first.parts[0].development);assert.equal(repeat.parts[0].scope.length,1);
 const verified=applyLiveWork(repeat,[{...source,milestones:source.milestones.map(()=>({done:true}))}]);assert.deepEqual(verified.parts[0].development,{completed:49,total:90});assert.equal(deliveryProgress(verified).parts[0].total_percent,43.6);assert.equal(deliveryProgress(verified).parts[0].acceptance_percent,0);
 assert.deepEqual(ledger.parts[0].development,{completed:45,total:86});
});
