"""Evidence gate only. Synthetic helper controls are not original-product test passes."""
from pathlib import Path
import xml.etree.ElementTree as ET
CLASS='Haven.Infrastructure.Tests.PlannerStructuredConcurrencyTests'
METHOD='Concurrent_writers_preserve_one_revision_and_deleted_assignments_require_restore'
SELECTED=CLASS+'.'+METHOD
EXPECTED={SELECTED,CLASS+'.Journey_event_authority_rechecks_calendar_permission_profile_and_revision'}
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def discovery_gate(text):
 names=[line.strip() for line in text.splitlines() if line.strip().startswith(CLASS+'.')]
 record={'actual_discovered_names':names,'actual_discovered_count':len(names),'expected_original_names':sorted(EXPECTED),'accepted':len(names)==2 and len(set(names))==2 and set(names)==EXPECTED}
 return record
def trx_gate(path,started_ns):
 path=Path(path);record={'path':str(path),'started_wall_ns':started_ns,'accepted':False,'violations':[]}
 if not path.exists():record['violations'].append('Fresh actual TRX absent');return record
 record['mtime_ns']=path.stat().st_mtime_ns
 if record['mtime_ns']<started_ns:record['violations'].append('TRX older than current invocation')
 try:
  root=ET.parse(path).getroot();summary=root.find('t:ResultSummary',NS);counter= root.find('t:ResultSummary/t:Counters',NS)
  if summary is None or counter is None:raise ValueError('TRX summary/counters absent')
  counts={key:int(value) for key,value in counter.attrib.items()};record['counters']=counts;record['summary_outcome']=summary.attrib.get('outcome')
  if any(counts.get(key)!=1 for key in ('total','executed','passed')):record['violations'].append('Original selected1 must be actually total1/executed1/passed1')
  negative=('failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','inProgress','pending')
  if any(counts.get(key)!=0 for key in negative):record['violations'].append('Failure/skip/nonexecution/warning counter nonzero or absent')
  if summary.attrib.get('outcome') not in ('Completed','Passed'):record['violations'].append('Actual TRX summary not successful')
  results=root.findall('t:Results/t:UnitTestResult',NS);definitions=root.findall('t:TestDefinitions/t:UnitTest',NS);entries=root.findall('t:TestEntries/t:TestEntry',NS)
  record['actual_result_count']=len(results);record['actual_result_names']=[x.attrib.get('testName') for x in results];record['actual_outcomes']=[x.attrib.get('outcome') for x in results]
  if len(results)!=1 or len(definitions)!=1 or len(entries)!=1:record['violations'].append('Exactly one actual result/definition/entry required')
  else:
   result,definition,entry=results[0],definitions[0],entries[0];method=definition.find('t:TestMethod',NS);execution=definition.find('t:Execution',NS)
   record['actual_test_method']=method.attrib if method is not None else None
   if result.attrib.get('testName')!=SELECTED or result.attrib.get('outcome')!='Passed':record['violations'].append('Result identity/outcome differs from original selected Fact')
   if method is None or method.attrib.get('className')!=CLASS or method.attrib.get('name')!=METHOD:record['violations'].append('Original TestMethod identity absent or mismatched')
   if not result.attrib.get('testId') or result.attrib.get('testId')!=definition.attrib.get('id') or result.attrib.get('testId')!=entry.attrib.get('testId'):record['violations'].append('Result/definition/entry testId linkage invalid')
   if not result.attrib.get('executionId') or result.attrib.get('executionId')!=entry.attrib.get('executionId') or execution is None or result.attrib.get('executionId')!=execution.attrib.get('id'):record['violations'].append('Actual execution linkage invalid')
 except (ET.ParseError,ValueError,TypeError,OSError) as error:record['violations'].append(repr(error))
 record['accepted']=not record['violations'];return record
