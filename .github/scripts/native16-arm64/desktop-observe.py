"""Bounded external AT-SPI observation; no application hooks or state writes."""
import argparse,json,time,sys
import pyatspi
parser=argparse.ArgumentParser();parser.add_argument('--pid',type=int);args=parser.parse_args()
rows=[];errors=[];limit=time.monotonic()+3;queue=[(pyatspi.Registry.getDesktop(0),'0',None)];visited=0
def process_id(node,inherited):
 for candidate in (node,):
  try:
   value=int(candidate.get_process_id())
   if value>0:return value
  except Exception:pass
 try:
  value=int(node.getApplication().get_process_id())
  if value>0:return value
 except Exception:pass
 return inherited
def identifier(node):
 for method in ('get_accessible_id','getAccessibleId'):
  try:
   value=getattr(node,method)()
   if value:return str(value)
  except Exception:pass
 try:
  for value in node.getAttributes():
   key,sep,item=value.partition(':')
   if sep and key in ('id','automation-id','accessible-id'):return item
 except Exception:pass
 return None
while queue and visited<700 and time.monotonic()<limit:
 n,path,inherited_pid=queue.pop(0);visited+=1
 try:
  name=n.name or '';role=n.getRoleName();text='';rect=None;state=n.getState();pid=process_id(n,inherited_pid)
  try:t=n.queryText();text=t.getText(0,min(t.characterCount,6000))
  except Exception:pass
  try:
   r=n.queryComponent().getExtents(pyatspi.DESKTOP_COORDS);rect=[r.x,r.y,r.width,r.height]
  except Exception:pass
  if args.pid is None or pid==args.pid:
   rows.append({'name':name,'role':role,'text':text,'rectangle':rect,'showing':state.contains(pyatspi.STATE_SHOWING),'enabled':state.contains(pyatspi.STATE_ENABLED),'process_id':pid,'accessible_id':identifier(n),'tree_path':path})
  queue.extend((n[i],path+'/'+str(i),pid) for i in range(min(n.childCount,300)))
 except Exception as e:
  if len(errors)<10:errors.append(type(e).__name__+': '+str(e))
if args.pid is not None and not rows:errors.append('No AT-SPI nodes with verified process_id '+str(args.pid)+'; unknown identity is not accepted')
print(json.dumps({'rows':rows,'errors':errors,'bounded':bool(queue),'requested_pid':args.pid,'visited':visited}))
