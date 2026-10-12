import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createAccountThemePreferences } from './account-theme-preferences.js';
import { validCustomThemeLibrary } from './theme-preference-contract.js';
import { AccountApiClient } from './account-api-client.js';

const A='11111111-1111-4111-8111-111111111111',B='22222222-2222-4222-8222-222222222222';
const preference=(id=A,revision=1)=>({schemaVersion:1,scope:{scope:1,ownerId:id},themeName:'Professional',revision,
    accessibility:{reduceMotion:false,highContrast:false,displayScale:1},appearance:'Dark',accent:{mode:0,allAppsColour:null,individualApps:{}},customThemesJson:'[]'});
function fixture() {
    const values=new Map(), requests=[];let owner=A, credential=true, offline=false, held=null;
    const storage={getItem:key=>values.get(key)??null,setItem:(key,value)=>values.set(key,value)};
    const client={getCurrent:async()=>({ok:true,body:{accountId:owner}}),getTheme:async id=>{
        requests.push(['get',id]);if(held)await held;if(offline)return {ok:false,status:null,error:{code:'TransportUnavailable'}};
        return {ok:true,body:{preference:preference(id)}};
    },updateTheme:async(id,revision,value)=>{requests.push(['set',id,revision]);if(held)await held;
        return offline?{ok:false,status:null,error:{code:'TransportUnavailable'}}:{ok:true,body:{preference:value}};
    }};
    const theme=createAccountThemePreferences({client,hasCredential:()=>credential,apiResource:'https://accounts.example.test',storage,window:null});
    return {theme,values,requests,setOwner:id=>{owner=id;},setCredential:value=>{credential=value;},setOffline:value=>{offline=value;},hold:promise=>{held=promise;}};
}
test('same account offline cache is readable only within a currently verified credential lifetime',async()=>{
    const f=fixture();assert.equal(await f.theme.themeContext('context'),A);
    f.theme.writeCache(A,JSON.stringify(preference()));f.setOffline(true);
    assert.equal(JSON.parse(await f.theme.readTheme('offline',A)).state,1);
    assert.deepEqual(JSON.parse(f.theme.readCache(A)).preference,preference());
    f.setCredential(false);
    assert.equal(JSON.parse(f.theme.readCache(A)).state,3);
    assert.equal(JSON.parse(await f.theme.compareTheme('denied',A,1,JSON.stringify(preference(A,2)))).succeeded,false);
    assert.equal(f.requests.filter(([kind])=>kind==='set').length,0);
    await f.theme.disposeAsync();
});
test('account switch clears observed preferences synchronously and never uploads old account cache',async()=>{
    const f=fixture();await f.theme.themeContext('a');f.theme.writeCache(A,JSON.stringify(preference()));
    let revoked=false;f.theme.subscribe(value=>{if(value){revoked=true;assert.equal(f.theme.canAccess(A),false);}});
    f.theme.invalidate();assert.equal(revoked,true);f.setOwner(B);await f.theme.themeContext('b');
    assert.equal(JSON.parse(f.theme.readCache(B)).state,1);assert.equal(JSON.parse(f.theme.readCache(A)).state,3);
    assert.equal(JSON.parse(await f.theme.compareTheme('wrong',B,0,JSON.stringify(preference(A)))).succeeded,false);
    assert.equal(f.requests.filter(([kind])=>kind==='set').length,0);assert.equal(f.values.size,1);
    await f.theme.disposeAsync();
});
test('late read and write responses from an invalidated account cannot become current',async()=>{
    const f=fixture();await f.theme.themeContext('a');let resolve;f.hold(new Promise(done=>resolve=done));
    const read=f.theme.readTheme('read',A),write=f.theme.compareTheme('write',A,0,JSON.stringify(preference()));
    await Promise.resolve();f.theme.invalidate();resolve();
    assert.equal(JSON.parse(await read).state,3);assert.equal(JSON.parse(await write).code,'ThemeOutcomeUnknown');
    assert.equal(f.values.size,0);await f.theme.disposeAsync();
});
test('failed remote save cannot update the offline cache or claim success',async()=>{
    const f=fixture();await f.theme.themeContext('a');f.theme.writeCache(A,JSON.stringify(preference()));f.setOffline(true);
    const reply=JSON.parse(await f.theme.compareTheme('write',A,1,JSON.stringify(preference(A,2))));
    assert.equal(reply.succeeded,false);assert.equal(reply.code,'ThemeOutcomeUnknown');assert.equal(JSON.parse(f.theme.readCache(A)).preference.revision,1);
    await f.theme.disposeAsync();
});
test('same account cache refuses revision regression and corrupt or foreign envelopes',async()=>{
    const f=fixture();await f.theme.themeContext('a');f.theme.writeCache(A,JSON.stringify(preference(A,3)));f.theme.writeCache(A,JSON.stringify(preference(A,1)));
    assert.equal(JSON.parse(f.theme.readCache(A)).preference.revision,3);
    assert.throws(()=>f.theme.writeCache(A,JSON.stringify(preference(B,4))));
    f.values.set([...f.values.keys()][0],JSON.stringify({version:1,apiResource:'https://wrong.example.test',preference:preference()}));
    assert.equal(JSON.parse(f.theme.readCache(A)).state,4);await f.theme.disposeAsync();
});
test('API theme updates snapshot caller data and reject a response for another account',async()=>{
    let releaseToken;const token=new Promise(resolve=>releaseToken=resolve);let sent;
    const api=new AccountApiClient({apiResource:'https://accounts.example.test',getAccessToken:()=>token,onPrivateContextInvalidated:()=>{},
        fetch:async(_url,options)=>{sent=JSON.parse(options.body);return Response.json({preference:preference(B)});}});
    const candidate=preference();const pending=api.updateTheme(A,0,candidate);candidate.themeName='Modified after admission';releaseToken('unit-test-token');
    const reply=await pending;assert.equal(sent.preference.themeName,'Professional');assert.equal(reply.error.code,'MalformedResponse');
});
test('portable custom library validates inactive branches, fonts, role coverage, duplicate names and size',()=>{
    const colour={hex:'#112233',transparencyPercent:15,backgroundBlur:4};const fill={kind:0,colour,gradient:null,pattern:null};const font={family:'Montserrat',weight:500,size:14};
    const theme={schemaVersion:1,id:A,name:'My theme',baseTheme:5,primaryAccent:fill,secondaryAccent:fill,tertiaryAccent:fill,
        fonts:Object.fromEntries(['main','primaryButton','secondaryButton','tertiaryButton','heading','body','code'].map(role=>[role,font])),buttonRoundnessPercent:35};
    assert.equal(validCustomThemeLibrary(JSON.stringify([theme])),true);
    const invalid=structuredClone(theme);invalid.primaryAccent.pattern={kind:0,foreground:colour,background:colour,size:0,spacing:8,angle:0};
    assert.equal(validCustomThemeLibrary(JSON.stringify([invalid])),false);
    invalid.primaryAccent.pattern=null;invalid.fonts.code.family='constructor';assert.equal(validCustomThemeLibrary(JSON.stringify([invalid])),false);
    invalid.fonts.code.family='https://font.example.test';assert.equal(validCustomThemeLibrary(JSON.stringify([invalid])),false);
    assert.equal(validCustomThemeLibrary(JSON.stringify([theme,{...theme,id:B,name:'MY THEME'}])),false);
    assert.equal(validCustomThemeLibrary(JSON.stringify([{...theme,name:'Professional'}])),false);
    assert.equal(validCustomThemeLibrary(JSON.stringify([{...theme,unknownCallback:'run()'}])),false);
});

test('unavailable browser storage cannot prevent authenticated service composition',async()=>{
    const unavailable=new Proxy({}, {get(){throw new Error('StorageDenied');}});
    const theme=createAccountThemePreferences({client:{getCurrent:async()=>({ok:true,body:{accountId:A}})},hasCredential:()=>true,apiResource:'https://accounts.example.test',storage:unavailable,window:null});
    assert.equal(await theme.themeContext('a'),A);assert.equal(JSON.parse(theme.readCache(A)).state,4);
    assert.throws(()=>theme.writeCache(A,JSON.stringify(preference())));await theme.disposeAsync();
});
