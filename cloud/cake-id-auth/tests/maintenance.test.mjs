import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';

const built = await build({entryPoints:['src/maintenance.ts'],bundle:true,write:false,platform:'node',format:'esm'});
const {purgeTransientData,TRANSIENT_RETENTION} = await import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'));
const now = Date.parse('2026-11-15T12:00:00.000Z'), day = 86400000, second = Math.floor(now/1000);

function fixture() {
  const db = new DatabaseSync(':memory:'); db.exec('PRAGMA foreign_keys=ON');
  for(const file of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql',
    '0003_turnstile_redemptions.sql','0004_account_invitations.sql','0005_public_account_policy.sql'])
    db.exec(readFileSync(new URL('../migrations/'+file,import.meta.url),'utf8'));
  let batchCalls=0, beforeBatch;
  const DB = {
    prepare(sql){return {bind(...values){return {sql,values};}};},
    async batch(statements){
      batchCalls++; beforeBatch?.(); db.exec('BEGIN');
      try {
        const result=statements.map(({sql,values})=>({success:true,results:[],meta:{changes:Number(db.prepare(sql).run(...values).changes)}}));
        db.exec('COMMIT'); return result;
      } catch(error) {db.exec('ROLLBACK'); throw error;}
    },
  };
  return {db,env:{DB,LOGIN_WINDOW_SECONDS:'900'},calls:()=>batchCalls,setBeforeBatch(fn){beforeBatch=fn;},close(){db.close();}};
}
const digest=(db,id,expires)=>db.prepare('INSERT INTO cake_turnstile_redemptions VALUES(?,?)').run(id,expires);
function invitation(db,id,{expires=now-day,consumed=null,revoked=null,created=now-40*day}={}) {
  db.prepare('INSERT INTO cake_account_invitations(id,tokenHash,email,createdAt,expiresAt,consumedAt,revokedAt) VALUES(?,?,?,?,?,?,?)')
    .run(id,'digest-'+id,id+'@example.test',created,expires,consumed,revoked);
}
function attempt(db,id,{start=second-86400-3600,locked=0,updated=second-86400}={}) {
  db.prepare('INSERT INTO cake_login_attempts VALUES(?,?,?,?,?)').run(id,start,8,locked,updated);
}
const ids=(db,table,column)=>db.prepare(`SELECT ${column} AS id FROM ${table} ORDER BY ${column}`).all().map(row=>row.id);

test('fixed-clock cleanup honors timestamp units and exact grace boundaries',async()=>{
  const f=fixture();try {
    digest(f.db,'old',now-day-1);digest(f.db,'at-boundary',now-day);digest(f.db,'within-grace',now-day+1);digest(f.db,'active',now+600000);
    invitation(f.db,'expired-old',{expires:now-30*day-1});invitation(f.db,'expired-boundary',{expires:now-30*day});
    invitation(f.db,'expired-recent',{expires:now-30*day+1});invitation(f.db,'active',{expires:now+day});
    attempt(f.db,'old');attempt(f.db,'window-in-grace',{start:second-86400-3600+1});
    attempt(f.db,'lock-in-grace',{locked:second-86400+1});attempt(f.db,'recent-write',{updated:second-86400+1});
    attempt(f.db,'locked-active',{locked:second+3600});attempt(f.db,'window-active',{start:second-10,updated:second-10});
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:2,invitations:2,loginAttemptCounters:1});
    assert.deepEqual(ids(f.db,'cake_turnstile_redemptions','tokenHash'),['active','within-grace']);
    assert.deepEqual(ids(f.db,'cake_account_invitations','id'),['active','expired-recent']);
    assert.deepEqual(ids(f.db,'cake_login_attempts','keyHash'),['lock-in-grace','locked-active','recent-write','window-active','window-in-grace']);
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:0,invitations:0,loginAttemptCounters:0});
  }finally{f.close();}
});

test('invitation cleanup uses consumed/revoked termination and preserves later terminal evidence',async()=>{
  const f=fixture();try {
    invitation(f.db,'consumed-old',{expires:now+day,consumed:now-31*day});
    invitation(f.db,'revoked-old',{expires:now+day,revoked:now-31*day});
    invitation(f.db,'consumed-new',{expires:now-40*day,consumed:now-day});
    invitation(f.db,'revoked-new',{expires:now-40*day,revoked:now-day});
    invitation(f.db,'two-markers',{expires:now-40*day,consumed:now-31*day,revoked:now-day});
    invitation(f.db,'future-marker',{expires:now-40*day,consumed:now+day});
    invitation(f.db,'created-recent',{expires:now-40*day,created:now-day});
    assert.equal((await purgeTransientData(f.env,now)).invitations,2);
    assert.deepEqual(ids(f.db,'cake_account_invitations','id'),['consumed-new','created-recent','future-marker','revoked-new','two-markers']);
  }finally{f.close();}
});

test('limiter cleanup respects configured longer login windows and the existing email window',async()=>{
  for(const setting of ['7200','900','invalid','-1']){
    const f=fixture();try {
      f.env.LOGIN_WINDOW_SECONDS=setting;
      attempt(f.db,'two-hour-old',{start:second-86400-7200});
      attempt(f.db,'one-hour-old',{start:second-86400-3600});
      const counts=await purgeTransientData(f.env,now);
      assert.equal(counts.loginAttemptCounters,setting==='7200'?1:2);
      assert.equal(f.db.prepare('SELECT count(*) n FROM cake_login_attempts').get().n,setting==='7200'?1:0);
    }finally{f.close();}
  }
});

test('each invocation deletes no more than 500 rows per table and repeated runs drain safely',async()=>{
  const f=fixture();try {
    for(let i=0;i<507;i++){
      digest(f.db,'digest-'+i,now-day-1);invitation(f.db,'invite-'+i,{expires:now-31*day});attempt(f.db,'attempt-'+i);
    }
    digest(f.db,'active-digest',now+600000);invitation(f.db,'active-invite',{expires:now+day});attempt(f.db,'active-attempt',{locked:second+3600});
    assert.equal(TRANSIENT_RETENTION.maximumRowsPerTable,500);
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:500,invitations:500,loginAttemptCounters:500});
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:7,invitations:7,loginAttemptCounters:7});
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:0,invitations:0,loginAttemptCounters:0});
    assert.equal(ids(f.db,'cake_turnstile_redemptions','tokenHash')[0],'active-digest');
    assert.equal(ids(f.db,'cake_account_invitations','id')[0],'active-invite');
    assert.equal(ids(f.db,'cake_login_attempts','keyHash')[0],'active-attempt');
  }finally{f.close();}
});

test('no user, profile, account, session, reservation, email, payment or audit data is deleted',async()=>{
  const f=fixture();try {
    const user='11111111-1111-4111-8111-111111111111';
    f.db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,pronouns) VALUES(?,?,?,?,?,?,?)').run(user,'Synthetic','test@example.test',1,'2020-01-01','2020-01-01','they/them');
    f.db.prepare('INSERT INTO session(id,userId,token,expiresAt,createdAt,updatedAt) VALUES(?,?,?,?,?,?)').run('old-session',user,'synthetic-token','2020-01-02','2020-01-01','2020-01-01');
    f.db.prepare('INSERT INTO account(id,accountId,providerId,userId,password,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?)').run('account','provider-account','credential',user,'synthetic-hash','2020-01-01','2020-01-01');
    f.db.prepare('INSERT INTO accountThemePreference VALUES(?,?,?,?)').run(user,1,JSON.stringify({revision:1,scope:{scope:1,ownerId:user},themeName:'preserve-theme'}),'2020-01-01');
    f.db.prepare('INSERT INTO cake_public_account_policy VALUES(?,?,?,?,?,?)').run(user,'18_plus','terms-fixture','privacy-fixture',1,1);
    f.db.prepare('INSERT INTO cake_email_outbox(recipient,subject,body,createdAt) VALUES(?,?,?,?)').run('test@example.test','old-email','synthetic-body',1);
    f.db.exec("CREATE TABLE retained_payment (id TEXT);INSERT INTO retained_payment VALUES('invoice');CREATE TABLE retained_audit(id TEXT);INSERT INTO retained_audit VALUES('event');");
    const tables=['user','session','account','accountThemePreference','cake_public_account_policy','cake_reserved_usernames','cake_email_outbox','retained_payment','retained_audit'];
    const before=tables.map(table=>JSON.stringify(f.db.prepare(`SELECT * FROM ${table}`).all()));
    digest(f.db,'expired',now-2*day);invitation(f.db,'expired',{expires:now-31*day});attempt(f.db,'expired');
    assert.deepEqual(await purgeTransientData(f.env,now),{turnstileDigests:1,invitations:1,loginAttemptCounters:1});
    assert.deepEqual(tables.map(table=>JSON.stringify(f.db.prepare(`SELECT * FROM ${table}`).all())),before);
  }finally{f.close();}
});

test('a limiter record refreshed before the batch is not removed by an earlier eligibility decision',async()=>{
  const f=fixture();try{
    attempt(f.db,'updated-concurrently');
    f.setBeforeBatch(()=>f.db.prepare('UPDATE cake_login_attempts SET windowStartedAt=?,updatedAt=?,lockedUntil=? WHERE keyHash=?')
      .run(second,second,second+3600,'updated-concurrently'));
    assert.equal((await purgeTransientData(f.env,now)).loginAttemptCounters,0);
  }finally{f.close();}
});

test('invalid clocks and failed batches never report success or expose record values',async()=>{
  const f=fixture();try{
    for(const invalid of [NaN,Infinity,-1,1.5,'2026-11-15'])await assert.rejects(purgeTransientData(f.env,invalid),/invalid_maintenance_clock/);
    assert.equal(f.calls(),0);
    const env={DB:{prepare(){return {bind(){return {};}};},async batch(){throw Error('private@example.test secret-digest');}}};
    await assert.rejects(purgeTransientData(env,now),error=>error.message==='transient_retention_failed');
    env.DB.batch=async()=>[{success:false,meta:{changes:0}}];
    await assert.rejects(purgeTransientData(env,now),/transient_retention_failed/);
  }finally{f.close();}
});
