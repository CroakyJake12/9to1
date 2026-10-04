#!/usr/bin/env python3
"""Controlled source-guard checks only; no browser/native/provider acceptance."""
import ast, copy, hashlib, json, os, shutil, stat, sys, tempfile
from pathlib import Path
SOURCE_SHA="6dd63065defe9a421de080198e2029266501f6a03d4d41dc23e5ad292250c026"
BASE_SHA="20fc542341b8d88ad1b2b96094dbbe33ecc6a0567378cbef7f3b0b3b5c3ba0ca"
def digest(data):return hashlib.sha256(data).hexdigest()
def namespace(text):
 tree=ast.parse(text)
 selected=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in {"sha","owned_regular","owner_output_guard"} or isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id in {"OWNER_ASSET_PREFIX","OWNER_ASSET_NAMES"} for t in n.targets)]
 ns={"hashlib":hashlib,"json":json,"Path":Path,"stat":stat,"re":__import__("re")}
 exec(compile(ast.Module(body=selected,type_ignores=[]),"<actual-production-helper-AST>","exec"),ns)
 return ns
class CommandBoundary:
 def __init__(self,text):self.text=text;self.calls=[]
 def run(self,name,argv,timeout):
  self.calls.append((name,argv,timeout))
  assert argv==["git","status","--porcelain=v1","-z","--untracked-files=all"] and timeout==15
  return self.text
def refused(fn):
 try:fn()
 except RuntimeError:return
 raise AssertionError("Expected strict rejection")
def main():
 source_path=Path(sys.argv[1]);base_path=Path(sys.argv[2]);output=Path(sys.argv[3])
 source=source_path.read_bytes();base=base_path.read_bytes()
 assert digest(source)==SOURCE_SHA and digest(base)==BASE_SHA
 ns=namespace(source.decode());guard=ns["owner_output_guard"];prefix=ns["OWNER_ASSET_PREFIX"];names=ns["OWNER_ASSET_NAMES"]
 output.parent.mkdir(parents=True,exist_ok=True)
 assert shutil.disk_usage(output.parent).free>268435456+49152
 outcomes=[];assertions=0
 def check(condition):
  nonlocal assertions
  assertions+=1
  if not condition:raise AssertionError("Source-guard assertion failed")
 def fixture(test):
  with tempfile.TemporaryDirectory(prefix="guard-",dir=output.parent) as td:
   root=Path(td)/"root";root.mkdir();publication=Path(td)/"publication";(publication/"diagnostics").mkdir(parents=True)
   rows=[]
   for name in names:
    rel=prefix+name;p=root/rel;p.parent.mkdir(parents=True,exist_ok=True);data=("controlled-producer-fixture:"+name).encode();p.write_bytes(data)
    rows.append({"path":rel,"bytes":len(data),"sha256":digest(data)})
   records=[{"name":"owner-assets-build","argv":[str(publication/"tools/bun"),"build.js"],"exit":0,"normalEOF":True,"familyClosed":True,"finalECHILD":True,"signals":[],"error":None,"births":[{"gone":True}]}]
   def persist():
    (publication/"diagnostics/owner-built-assets.json").write_text(json.dumps(rows))
    (publication/"diagnostics/commands.json").write_text(json.dumps(records))
   persist()
   rv={"publishFiles":[{"path":"_framework/"+name,"bytes":rows[names.index(name)]["bytes"],"sha256":rows[names.index(name)]["sha256"]} for name in ("avalonia.js","storage.js")]}
   cmd=CommandBoundary("".join("?? "+prefix+x+"\0" for x in names))
   test(root,publication,rows,records,persist,rv,cmd)
 def execute(name,test):
  try:fixture(test);outcomes.append({"name":name,"state":"PASS"})
  except Exception as error:outcomes.append({"name":name,"state":"FAIL","type":type(error).__name__,"message":str(error)})
 def run(root,pub,rv,cmd,prior=None):return guard(root,pub,rv,cmd,"controlled-status",prior)
 def positive(root,pub,rows,records,persist,rv,cmd):
  value=run(root,pub,rv,cmd);check(set(value)=={prefix+n for n in names});check(len(cmd.calls)==1)
  original=next(n for n in ast.walk(ast.parse(base.decode())) if isinstance(n,ast.If) and any(isinstance(c,ast.Constant) and c.value=="Fresh source graph required" for c in ast.walk(n)))
  original_cmd=type("OriginalBoundary",(),{"run":lambda self,*args:"".join("?? "+prefix+x+"\n" for x in names)})()
  refused(lambda:exec(compile(ast.Module(body=[original],type_ignores=[]),"<immutable-original-guard>","exec"),{"cmd":original_cmd}))
  check(True)
 execute("authentic-six-and-immutable-original-refusal",positive)
 def unknown(root,pub,rows,records,persist,rv,cmd):
  (root/"unknown.dat").write_bytes(b"x");cmd.text+="?? unknown.dat\0";refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("unknown-additional-input",unknown)
 def tamper(root,pub,rows,records,persist,rv,cmd):
  p=root/(prefix+"avalonia.js.map");before=p.read_bytes();p.write_bytes(bytes([before[0]^1])+before[1:]);check(len(p.read_bytes())==len(before))
  refused(lambda:run(root,pub,rv,cmd));check(True)
  mutant=source.decode().replace(' or sha(path)!=row["sha256"]','')
  check(mutant!=source.decode())
  accepted=namespace(mutant)["owner_output_guard"](root,pub,rv,cmd,"controlled-status")
  check(len(accepted)==6)
 execute("same-length-tamper-and-digest-removal-mutant",tamper)
 def duplicate(root,pub,rows,records,persist,rv,cmd):
  rows[-1]=copy.deepcopy(rows[0]);persist();refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("duplicate-producer-row",duplicate)
 def incomplete(root,pub,rows,records,persist,rv,cmd):
  rows.pop();persist();refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("incomplete-producer-set",incomplete)
 def extra(root,pub,rows,records,persist,rv,cmd):
  rows.append({"path":prefix+"extra.js","bytes":0,"sha256":digest(b"")});persist();refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("extra-producer-row",extra)
 def alias(root,pub,rows,records,persist,rv,cmd):
  rows[0]["path"]=prefix+"./"+names[0];persist();refused(lambda:run(root,pub,rv,cmd));refused(lambda:ns["owned_regular"](root,prefix+"./"+names[0]));check(True)
 execute("noncanonical-path",alias)
 def link(root,pub,rows,records,persist,rv,cmd):
  p=root/(prefix+names[0]);held=root/"held.bin";p.rename(held);p.symlink_to(held);refused(lambda:run(root,pub,rv,cmd));check(True)
  p.unlink();held.rename(p);directory=p.parent;held_dir=root/"held-assets";directory.rename(held_dir);directory.symlink_to(held_dir,target_is_directory=True)
  refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("symlink-output-and-ancestor",link)
 def nonregular(root,pub,rows,records,persist,rv,cmd):
  p=root/(prefix+names[0]);p.unlink();os.mkfifo(p);refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("nonregular-output",nonregular)
 def changed(root,pub,rows,records,persist,rv,cmd):
  cmd.text=cmd.text.replace("?? "," M ",1);refused(lambda:run(root,pub,rv,cmd));check(True)
  cmd.text="R  "+prefix+names[0]+"\0former\0";refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("tracked-and-rename-status",changed)
 def truncated(root,pub,rows,records,persist,rv,cmd):
  cmd.text=cmd.text[:-1];refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("incomplete-status-framing",truncated)
 def failed(root,pub,rows,records,persist,rv,cmd):
  records[0]["exit"]=1;persist();refused(lambda:run(root,pub,rv,cmd));check(True)
  records[0]["exit"]=0;records[0]["argv"][1]="other.js";persist();refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("producer-build-failed-or-wrong-command",failed)
 def sealed(root,pub,rows,records,persist,rv,cmd):
  rv["publishFiles"][0]["sha256"]="0"*64;refused(lambda:run(root,pub,rv,cmd));check(True)
 execute("sealed-module-disagreement",sealed)
 def drift(root,pub,rows,records,persist,rv,cmd):
  prior=run(root,pub,rv,cmd);p=root/(prefix+"avalonia.js.map");body=p.read_bytes();p.write_bytes(bytes([body[0]^1])+body[1:]);rows[1]["sha256"]=digest(p.read_bytes());persist()
  refused(lambda:run(root,pub,rv,cmd,prior));check(True)
 execute("post-run-producer-pin-drift",drift)
 report={"state":"PASS_SOURCE_GUARD_CONTROLS" if all(x["state"]=="PASS" for x in outcomes) else "FAIL","counts":{"declared":14,"executed":len(outcomes),"passed":sum(x["state"]=="PASS" for x in outcomes),"failed":sum(x["state"]=="FAIL" for x in outcomes)},"assertions":assertions,"sourceSHA256":digest(source),"immutableOriginalSHA256":digest(base),"outcomes":outcomes,"qualification":"Controlled source guard only: six real synthetic files and controlled command receipts; no browser/native/provider acceptance or actual producer replay.","fixtureDirectoriesRemoved":not any(output.parent.glob("guard-*"))}
 raw=(json.dumps(report,indent=2)+"\n").encode();assert len(raw)<16384 and shutil.disk_usage(output.parent).free-len(raw)>268435456
 output.write_bytes(raw);print(json.dumps(report));return 0 if report["state"]=="PASS_SOURCE_GUARD_CONTROLS" else 1
if __name__=="__main__":raise SystemExit(main())
