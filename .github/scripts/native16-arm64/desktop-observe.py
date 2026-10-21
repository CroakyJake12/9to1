"""Bounded external AT-SPI observation; no application hooks or state writes."""
import json,time,sys
import pyatspi
rows=[];errors=[];limit=time.monotonic()+3;queue=[pyatspi.Registry.getDesktop(0)]
while queue and len(rows)<700 and time.monotonic()<limit:
 n=queue.pop(0)
 try:
  name=n.name or '';role=n.getRoleName();text='';rect=None;state=n.getState()
  try:t=n.queryText();text=t.getText(0,min(t.characterCount,6000))
  except Exception:pass
  try:
   r=n.queryComponent().getExtents(pyatspi.DESKTOP_COORDS);rect=[r.x,r.y,r.width,r.height]
  except Exception:pass
  rows.append({'name':name,'role':role,'text':text,'rectangle':rect,'showing':state.contains(pyatspi.STATE_SHOWING),'enabled':state.contains(pyatspi.STATE_ENABLED)})
  queue.extend(n[i] for i in range(min(n.childCount,300)))
 except Exception as e:
  if len(errors)<10:errors.append(type(e).__name__+': '+str(e))
print(json.dumps({'rows':rows,'errors':errors,'bounded':bool(queue)}))
