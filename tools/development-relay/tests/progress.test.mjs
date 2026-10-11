import test from 'node:test';
import assert from 'node:assert/strict';
import {deliveryProgress} from '../progress.mjs';
test('Every part combines actual development counts at 80% and independent acceptance counts at 20%',()=>{
 const p=deliveryProgress({parts:[{part:1,development:{completed:6,total:10},acceptance:{completed:1,total:4}},{part:2,development:{completed:10,total:10},acceptance:{completed:0,total:4}},{part:3,development:{completed:10,total:10},acceptance:{completed:4,total:4}},{part:4,development:{completed:0,total:10},acceptance:{completed:0,total:4}}]},[{id:'live',part:1,status:'WORKING',executor:'strata'},{id:'failed',part:1,status:'BLOCKED'}]).parts;
 assert.equal(p[0].total_percent,53);assert.equal(p[0].development_percent,60);assert.equal(p[0].acceptance_percent,25);assert.equal(p[1].total_percent,80);assert.equal(p[2].total_percent,100);assert.equal(p[3].total_percent,0);assert.deepEqual(p[0].active_tasks.map(t=>t.id),['live']);
});
test('Missing scope or invalid evidence counts stay unmeasured instead of fabricating zero or completion',()=>{
 assert.equal(deliveryProgress(null).parts[0].total_percent,null);
 for(const development of [{completed:11,total:10},{completed:-1,total:10},{completed:0,total:0},{completed:Infinity,total:10}])assert.equal(deliveryProgress({parts:[{part:1,development,acceptance:{completed:0,total:10}}]}).parts[0].total_percent,null);
});
test('Fractional and unsafe integer milestone or acceptance counts are not accepted as progress',()=>{
 for(const development of [{completed:0.5,total:10},{completed:1,total:2.5},{completed:NaN,total:10},{completed:3,total:2**53},{completed:2**53,total:2**53+1}])assert.equal(deliveryProgress({parts:[{part:1,development,acceptance:{completed:0,total:10}}]}).parts[0].total_percent,null);
 assert.equal(deliveryProgress({parts:[{part:1,development:{completed:0,total:10},acceptance:{completed:0.5,total:10}}]}).parts[0].total_percent,null);
 assert.equal(deliveryProgress({parts:[{part:1,development:{completed:0,total:10},acceptance:{completed:0,total:10}}]}).parts[0].development_percent,0);
});
test('An unassessed scope remains Unknown even when a snapshot contains zero counters',()=>{
 const p=deliveryProgress({parts:[{part:3,development:{completed:0,total:28},acceptance:{completed:0,total:7},scope:[{assessed:false}]}]}).parts[2];
 assert.equal(p.assessment,'UNASSESSED');assert.equal(p.development_percent,null);assert.equal(p.acceptance_percent,null);assert.equal(p.total_percent,null);
});
