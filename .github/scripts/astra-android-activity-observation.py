"""Fail-closed Android activity observation: normalized components, actual task membership."""
import re
PACKAGE='com.cakemods.haven'
PROBE=PACKAGE+'.AstraCredentialProbeActivity'
COMPONENT=re.compile(r'(?<![\w.])([A-Za-z][\w.]*)/([.A-Za-z_$][\w.$]*)(?![\w.])')
def normalize_component(package,klass):
 if klass.startswith('.'):klass=package+klass
 elif '.' not in klass:klass=package+'.'+klass
 return package,klass
def inspect_activity(text):
 probe=[];main=[]
 for line in text.splitlines():
  components=[normalize_component(*match.groups()) for match in COMPONENT.finditer(line)]
  actualProbe=(PACKAGE,PROBE) in components
  # A bare full class or otherwise unrecognized reference cannot establish absence.
  unknownProbe=PROBE in line or 'AstraCredentialProbeActivity' in line
  if actualProbe or unknownProbe:
   historical=bool(re.fullmatch(r'\s*mLastPausedActivity:\s*ActivityRecord\{[^{}]*\bt-1\s+f\}\}?\s*',line)) and actualProbe
   probe.append({'line':line.strip(),'classification':'detached-historical' if historical else 'active-or-unknown'})
  resumed=bool(re.match(r'\s*(?:topResumedActivity=|mResumedActivity:|ResumedActivity:)',line))
  task=re.search(r'\bt([0-9]+)\b',line)
  if resumed and 'ActivityRecord{' in line and task and any(pkg==PACKAGE and klass.endswith('.MainActivity') for pkg,klass in components):
   # Finishing or detached record is not a positive foreground observation.
   if not re.search(r'\bt-1\b|\bt[0-9]+\s+f\b',line):main.append(line.strip())
 return {'probeEntries':probe,'activeOrUnknownProbe':any(x['classification']!='detached-historical' for x in probe),'mainResumed':bool(main),'mainEntries':main}
