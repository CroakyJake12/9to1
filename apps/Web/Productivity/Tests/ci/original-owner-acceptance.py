"""Source-pinned original Canvas8/Maps6 test identities; no work on import."""
from pathlib import Path
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
CODEC = "HavenOS.Apps.Canvas.Tests.CanvasArtifactCodecTests"
BRIDGE = "HavenOS.Apps.Canvas.Tests.CanvasFilesArtifactBridgeTests"
MAPS = "Haven.Infrastructure.Tests.MapsJourneyAuthoringCaptureTests"
EXPECTED = {
 "canvas8": [
  CODEC + ".Native_format_round_trip_preserves_stable_identity_mode_and_structured_ink(mode: Infinite)",
  CODEC + ".Native_format_round_trip_preserves_stable_identity_mode_and_structured_ink(mode: Paged)",
  CODEC + ".Unknown_optional_fields_survive_decode_and_reencode",
  CODEC + ".Unsupported_format_and_schema_versions_are_rejected_without_guessing",
  CODEC + ".Invalid_page_object_and_layer_references_are_reported_as_typed_validation_issues",
  CODEC + ".Paged_mode_requires_explicit_positive_page_bounds",
  CODEC + ".Malformed_json_has_a_stable_invalid_document_error",
  BRIDGE + ".Canonical_Files_create_edit_restart_reopen_preserves_identity_and_rejects_stale_readonly_and_tampered_content",
 ],
 "maps6": [
  MAPS + ".Lying_step_or_path_count_stops_at_first_excess_value_before_actual_storage(path: False)",
  MAPS + ".Lying_step_or_path_count_stops_at_first_excess_value_before_actual_storage(path: True)",
  MAPS + ".Held_save_freezes_ordered_authored_intent_and_user_path_before_actual_storage_await",
  MAPS + ".Later_caller_edits_do_not_change_returned_saved_revision_or_reopened_route",
  MAPS + ".Prepared_old_edit_cannot_overwrite_an_independent_actual_saved_revision",
  MAPS + ".Invalid_coordinates_return_structured_failure_without_serializing_or_replacing_saved_route",
 ]}

def discovery_gate(text, suite):
 expected = EXPECTED[suite]; classes = {name.split("(", 1)[0].rsplit(".", 1)[0] for name in expected}
 names = [line.strip() for line in text.splitlines() if any(line.strip().startswith(cls + ".") for cls in classes)]
 return {"actualNames": names, "expectedNames": expected, "actualCount": len(names),
         "accepted": len(names) == len(expected) and len(set(names)) == len(expected) and set(names) == set(expected)}

def trx_gate(path, started_ns, suite):
 path = Path(path); expected = EXPECTED[suite]; count = len(expected)
 result = {"accepted": False, "path": str(path), "startedWallNs": started_ns, "violations": []}
 if path.is_symlink() or not path.is_file():
  result["violations"].append("Fresh regular actual TRX absent"); return result
 result["mtimeNs"] = path.stat().st_mtime_ns
 if result["mtimeNs"] < started_ns: result["violations"].append("TRX predates this native invocation")
 try:
  doc = ET.parse(path).getroot(); summary = doc.find("t:ResultSummary", NS); counter = doc.find("t:ResultSummary/t:Counters", NS)
  if summary is None or counter is None: raise ValueError("Actual summary/counters absent")
  counts = {key: int(value) for key, value in counter.attrib.items()}; result["counters"] = counts; result["summaryOutcome"] = summary.get("outcome")
  if any(counts.get(key) != count for key in ("total", "executed", "passed")): result["violations"].append("Exact original total/executed/passed required")
  negative = ("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending")
  if any(counts.get(key) != 0 for key in negative): result["violations"].append("Error/skip/nonexecution/warning counter nonzero or absent")
  if summary.get("outcome") not in ("Completed", "Passed"): result["violations"].append("Actual summary did not complete")
  records = doc.findall("t:Results/t:UnitTestResult", NS); definitions = doc.findall("t:TestDefinitions/t:UnitTest", NS); entries = doc.findall("t:TestEntries/t:TestEntry", NS)
  result["actualNames"] = [x.get("testName") for x in records]; result["actualOutcomes"] = [x.get("outcome") for x in records]
  if len(records) != count or len(definitions) != count or len(entries) != count: result["violations"].append("Exact original result/definition/entry count required")
  if len(set(result["actualNames"])) != count or set(result["actualNames"]) != set(expected): result["violations"].append("Exact original display names and theory parameters required")
  identifiers = [d.get("id") for d in definitions]; tests = [x.get("testId") for x in records]; executions = [x.get("executionId") for x in records]
  if any(not v for v in identifiers + tests + executions) or any(len(set(values)) != count for values in (identifiers, tests, executions)): result["violations"].append("Unique nonempty actual test/execution identities required")
  by_id = {d.get("id"): d for d in definitions}; links = []
  for record in records:
   name = record.get("testName", ""); definition = by_id.get(record.get("testId"))
   if definition is None or name not in expected: result["violations"].append("Unexpected result or absent definition"); continue
   cls, display_method = name.rsplit(".", 1); base_method = display_method.split("(", 1)[0]
   method = definition.find("t:TestMethod", NS); execution = definition.find("t:Execution", NS)
   # VSTest adapters may bind TestMethod.name to base method or its exact display
   # method; full result/definition names above retain every reviewed theory value.
   if (record.get("outcome") != "Passed" or definition.get("name") != name or method is None
       or method.get("className") != cls or method.get("name") not in (base_method, display_method)):
    result["violations"].append("Original method identity/outcome mismatch: " + name)
   linked = [e for e in entries if e.get("testId") == record.get("testId") and e.get("executionId") == record.get("executionId")]
   if len(linked) != 1 or execution is None or execution.get("id") != record.get("executionId"): result["violations"].append("Actual definition/entry/execution linkage differs: " + name)
   links.append({"name": name, "testId": record.get("testId"), "executionId": record.get("executionId"), "method": None if method is None else method.attrib})
  result["actualIdentityLinks"] = links
 except (ET.ParseError, ValueError, TypeError, OSError) as error: result["violations"].append(repr(error))
 result["accepted"] = not result["violations"]; return result
