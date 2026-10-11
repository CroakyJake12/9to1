import test from 'node:test';
import assert from 'node:assert/strict';
import {initialState,selectShift,transition,pathsOverlap,normalizePaths} from '../policy.mjs';
const days=['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'];
const policy={timezone:'Europe/London',weekdays:Object.fromEntries(days.map(d=>[d,{local_start:'09:00',local_end:'17:00'}]))};
const available={home:true,healthy:true,paused:false};
for(let i=0;i<7;i++)for(const [hour,minute,expected] of [[8,59,'cloud'],[9,0,'local'],[16,59,'local'],[17,0,'cloud']]){
  test(`${days[i]} boundary ${hour}:${minute}`,()=>assert.equal(selectShift(policy,available,new Date(Date.UTC(2026,9,12+i,hour-1,minute))).shift,expected));
}
for(const day of [12,13,14,15,16])test(`No pre-college start on October ${day}`,()=>assert.equal(selectShift(policy,available,new Date(`2026-10-${day}T06:00:00Z`)).shift,'cloud'));
test('Home/Away and pause cannot bypass schedule',()=>{
  const at=new Date('2026-10-12T10:00:00Z');assert.equal(selectShift(policy,{...available,home:false},at).shift,'cloud');
  assert.equal(selectShift(policy,{...available,paused:true},at).shift,'cloud');assert.equal(selectShift(policy,{...available,healthy:false},at).shift,'cloud');
  assert.equal(selectShift(policy,available,new Date('2026-10-12T05:00:00Z')).shift,'cloud');
});
test('DST spring and autumn retain London wall-clock boundaries',()=>{
  for(const [at,expected] of [['2026-03-28T09:00:00Z','local'],['2026-03-29T08:00:00Z','local'],['2026-10-24T08:00:00Z','local'],['2026-10-25T09:00:00Z','local'],['2026-10-25T08:59:00Z','cloud']])assert.equal(selectShift(policy,available,new Date(at)).shift,expected);
});
test('Date overrides change holidays without changing source',()=>{
  const p={...policy,date_overrides:{'2026-10-12':{local_start:'07:30',local_end:'17:00'},'2026-10-13':{local_start:'09:00',local_end:'17:00',disabled:true}}};
  assert.equal(selectShift(p,available,new Date('2026-10-12T07:00:00Z')).shift,'local');assert.equal(selectShift(p,available,new Date('2026-10-13T10:00:00Z')).shift,'cloud');
});
test('Paths normalize and reject traversal and Windows escape',()=>{
  assert.ok(pathsOverlap(normalizePaths(['framework/CUI']),normalizePaths(['framework/cui/runtime'])));
  for(const paths of [['../x'],['C:/x'],['/x'],['x\\y'],['x/./y'],[]])assert.throws(()=>normalizePaths(paths));
});
const now=Date.parse('2026-10-12T10:00:00Z');
function state(){const s=initialState();transition(s,'policy',{policy},'local',now);transition(s,'presence',{...available},'local',now);return s;}
function queue(s,id='one',paths=['framework/CUI']){transition(s,'enqueue',{task:{id,paths,objective:'Bounded feature',source:{sha:'a'.repeat(40),branch:'relay/source'},tests:[{executable:'node',args:['--test']}] }},'local',now);}
test('Exclusive acquisition rejects duplicate owner and other shift',()=>{const s=state();transition(s,'acquire',{},'local',now);assert.throws(()=>transition(s,'acquire',{},'local',now));assert.throws(()=>transition(s,'acquire',{},'cloud',now));});
test('Expired lease increments fence and quarantines source',()=>{const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);transition(s,'presence',available,'local',now+100000);transition(s,'acquire',{},'local',now+100000);assert.equal(s.lease.fence,2);assert.equal(s.tasks[0].status,'RECOVERY_REQUIRED');assert.throws(()=>transition(s,'result',{fence:1,task_id:'one',status:'COMPLETED',evidence:[]},'local',now+100000));});
test('Overlapping task claims are rejected even in separate worktrees',()=>{const s=state();queue(s);queue(s,'two',['framework/CUI/Runtime']);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);assert.throws(()=>transition(s,'claim',{fence:1,task_id:'two'},'local',now));});
test('Offline presence gives cloud the schedule but retains owned task quarantine',()=>{const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);transition(s,'acquire',{},'cloud',now+100000);assert.equal(s.lease.worker,'cloud');assert.equal(s.tasks[0].status,'RECOVERY_REQUIRED');});
test('Pause and drain prevent further task assignments',()=>{const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'checkpoint',{},'local',now);assert.throws(()=>transition(s,'claim',{fence:1,task_id:'one'},'local',now));});
function ready(){const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);transition(s,'result',{fence:1,task_id:'one',status:'CHECKPOINT',evidence:[],result:{}},'local',now);return s;}
const h={id:'handover-1',from:'local',to:'cloud',tasks:['one'],source:{repository:'CroakyJake12/9to1',sha:'b'.repeat(40),branch:'relay/one'},tests:[],blockers:[]};
test('Failed remote verification cannot publish READY',()=>{const s=ready();assert.throws(()=>transition(s,'handover',{fence:1,handover:h,remote_verified:false},'local',now));assert.equal(s.handovers.length,0);assert.equal(s.lease.worker,'local');});
test('READY retains ownership; ACK requires exact source, revision and receiver',()=>{
  const s=ready();transition(s,'handover',{fence:1,handover:h,remote_verified:true},'local',now);assert.equal(s.lease.worker,'local');
  const ack={handover_id:h.id,revision:1,reconstructed_sha:h.source.sha,remote_verified:true};
  for(const p of [{...ack,revision:2},{...ack,reconstructed_sha:'c'.repeat(40)},{...ack,remote_verified:false}])assert.throws(()=>transition(s,'ack',p,'cloud',now));
  assert.throws(()=>transition(s,'ack',ack,'local',now));transition(s,'ack',ack,'cloud',now);assert.equal(s.lease,null);assert.equal(s.tasks[0].status,'QUEUED');assert.equal(s.handovers[0].status,'ACKNOWLEDGED');assert.throws(()=>transition(s,'ack',ack,'cloud',now));
});
test('Unfinished writer cannot hand over or release',()=>{const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);assert.throws(()=>transition(s,'handover',{fence:1,handover:h,remote_verified:true},'local',now));assert.throws(()=>transition(s,'release',{fence:1},'local',now));});
test('Implementer cannot promote independent, RCVP or FULL acceptance',()=>{const s=state();queue(s);transition(s,'acquire',{},'local',now);transition(s,'claim',{fence:1,task_id:'one'},'local',now);for(const acceptance of ['RCVP_VERIFIED','FULL_VERIFIED','INDEPENDENT_ACCEPTANCE_PASSED'])assert.throws(()=>transition(s,'result',{fence:1,task_id:'one',status:'COMPLETED',evidence:[],acceptance},'local',now));});
test('Production defaults to cloud without transmitting a timetable',()=>{const s=initialState();transition(s,'presence',{home:true,healthy:true,local_permitted:false,local_until:0},'local',now);assert.throws(()=>transition(s,'acquire',{},'local',now));transition(s,'acquire',{},'cloud',now);assert.equal(s.lease.worker,'cloud');assert.equal(s.policy,null);});
test('Private evaluated eligibility grants local only until the signed deadline',()=>{const s=initialState();transition(s,'presence',{home:true,healthy:true,local_permitted:true,local_until:now+30000},'local',now);transition(s,'acquire',{},'local',now);transition(s,'release',{fence:1},'local',now);assert.throws(()=>transition(s,'acquire',{},'local',now+31000));transition(s,'acquire',{},'cloud',now+31000);assert.equal(s.lease.worker,'cloud');});
test('Eligibility cannot be indefinite or be set by the cloud identity',()=>{const s=initialState();for(const local_until of [NaN,Infinity,now+90001])assert.throws(()=>transition(s,'presence',{local_permitted:true,local_until},'local',now));assert.throws(()=>transition(s,'presence',{home:true,healthy:true,local_permitted:true,local_until:now+30000},'cloud',now));});
