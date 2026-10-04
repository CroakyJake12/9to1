"""Observed CI caller custody, with only separately recorded inner native sessions.
This is not production authority or an atomic/unobserved-escape guarantee. Unknown
session changes refuse the final seal. The existing inner owner drains its own
session; this caller never fabricates an inner receipt or signals numeric IDs.
"""
import json,pathlib
from astra_original_native_session_drain import OriginalSession
class MailCallerSession(OriginalSession):
 def __init__(self,process,records,nativeRecords):
  self.nativeRecords=pathlib.Path(nativeRecords);self.innerAttempts={};super().__init__(process,records)
 def native_receipt(self,pid,expected):
  path=self.nativeRecords/(str(pid)+'-pending.json')
  try:
   if path.is_symlink() or not path.is_file() or path.stat().st_size>1024*1024:return None
   row=json.loads(path.read_text())
  except (OSError,ValueError):return None
  original=row.get('launcherTuple');members=row.get('observedMembers')
  if row.get('launcherPid')!=pid or not isinstance(original,list) or len(original)!=5 or not isinstance(members,dict):return None
  if original[0]!=expected[0] or original[1:4]!=[pid,pid,expected[3]]:return None
  member=members.get(str(pid))
  if not isinstance(member,list) or len(member)!=5 or member[:4]!=original[:4]:return None
  return row
 def inner_drained(self):
  for pid,expected in self.innerAttempts.items():
   receipt=self.native_receipt(pid,expected)
   if receipt is None or receipt.get('drained') is not True:return False
   for key,value in receipt['observedMembers'].items():
    if not key.isdigit() or not isinstance(value,list) or len(value)!=5:return False
    actual=self.stat(int(key))
    if actual is not None and actual[3]==value[3]:return False
  return True
 def write(self,drained):
  if drained and not self.inner_drained():raise RuntimeError('Known separately issued native session drain unproven; preserving caller false seal')
  super().write(drained)
  evidence=self.records/'inner-native-delegation.json'
  evidence.write_text(json.dumps({'observedInnerAttempts':self.innerAttempts,'allInnerRecordedDrainsProven':self.inner_drained(),'qualification':'Sampled original descendant/kernel birth identity and exact inner owner records; no atomic/unobserved-escape or production authority claim'},sort_keys=True)+'\n')
 def observe(self):
  current=self.stat(self.root)
  if current is not None and current[3]!=self.original[3]:raise RuntimeError('Original caller PID reused')
  for pid,expected in list(self.members.items()):
   actual=self.stat(pid)
   if actual is not None and actual[3]==expected[3] and actual[1:3]!=(self.root,self.root):raise RuntimeError('Original caller member escaped its session; retaining trust')
  rows={}
  for path in pathlib.Path('/proc').iterdir():
   if not path.name.isdigit():continue
   pid=int(path.name);row=self.stat(pid)
   if row is None:continue
   if row[2]==self.root:rows[pid]=row
   elif row[0] in self.members:
    parent=self.stat(row[0])
    if parent is not None and parent[3]==self.members[row[0]][3]:
     # Only separately recorded self-session roots can become delegated custody.
     # Capture the real original tuple now; lack of the inner record never seals.
     if row[1:3]!=(pid,pid):raise RuntimeError('Unrecognized original descendant session change')
     previous=self.innerAttempts.get(pid)
     if previous is not None and previous[3]!=row[3]:raise RuntimeError('Observed inner root PID reused before drain')
     self.innerAttempts.setdefault(pid,row)
  if rows and not any(pid in self.members and row[3]==self.members[pid][3] for pid,row in rows.items()):raise RuntimeError('Caller session has no original surviving identity anchor')
  for pid,row in rows.items():
   if row[1]!=self.root:raise RuntimeError('Original caller member changed group')
   self.members[pid]=row
  self.write(False);return rows
