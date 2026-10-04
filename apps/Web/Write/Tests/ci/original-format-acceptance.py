"""Strict original seven Fact identities; no execution or imports on module load."""
from pathlib import Path
import xml.etree.ElementTree as ET

EXPECTED = {
    "Haven.Infrastructure.Tests.WriteDocxFidelityTests.Rich_docx_export_emits_word_formatting_and_round_trips_supported_features",
    "Haven.Infrastructure.Tests.WriteDocxFidelityTests.Docx_import_reads_independent_wordprocessingml_formatting_and_external_link_metadata",
    "Haven.Infrastructure.Tests.WriteNativeDocumentPackageStoreTests.Save_and_open_preserve_structured_document_identity_and_content",
    "Haven.Infrastructure.Tests.WriteNativeDocumentPackageStoreTests.Open_rejects_changed_document_bytes_without_returning_a_partial_document",
    "Haven.Infrastructure.Tests.WriteNativeDocumentPackageStoreTests.Open_rejects_unsupported_manifest_version",
    "Haven.Infrastructure.Tests.WriteNativeDocumentPackageStoreTests.Failed_save_keeps_existing_destination_intact",
    "Haven.Infrastructure.Tests.WriteNativeDocumentPackageStoreTests.Open_rejects_traversal_entries",
}
CLASSES = {name.rsplit(".", 1)[0] for name in EXPECTED}
FILTER = "|".join("FullyQualifiedName=" + name for name in sorted(EXPECTED))
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def discovery_gate(text):
    names = [line.strip() for line in text.splitlines()
             if any(line.strip().startswith(cls + ".") for cls in CLASSES)]
    return {"actual_discovered_names": names, "actual_discovered_count": len(names),
            "expected_original_names": sorted(EXPECTED),
            "accepted": len(names) == 7 and len(set(names)) == 7 and set(names) == EXPECTED}


def trx_gate(path, started_ns):
    path = Path(path)
    record = {"path": str(path), "started_wall_ns": started_ns,
              "accepted": False, "violations": []}
    if not path.is_file():
        record["violations"].append("Fresh actual TRX absent")
        return record
    record["mtime_ns"] = path.stat().st_mtime_ns
    if record["mtime_ns"] < started_ns:
        record["violations"].append("TRX older than current invocation")
    try:
        root = ET.parse(path).getroot()
        summary = root.find("t:ResultSummary", NS)
        counter = root.find("t:ResultSummary/t:Counters", NS)
        if summary is None or counter is None:
            raise ValueError("Actual summary/counters absent")
        counts = {key: int(value) for key, value in counter.attrib.items()}
        record["counters"] = counts
        record["summary_outcome"] = summary.attrib.get("outcome")
        if any(counts.get(key) != 7 for key in ("total", "executed", "passed")):
            record["violations"].append("Original seven must actually total7/executed7/passed7")
        negative = ("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                    "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending")
        if any(counts.get(key) != 0 for key in negative):
            record["violations"].append("Failure/skip/nonexecution/warning counter nonzero or absent")
        if summary.attrib.get("outcome") not in ("Completed", "Passed"):
            record["violations"].append("Actual summary outcome unsuccessful")
        results = root.findall("t:Results/t:UnitTestResult", NS)
        definitions = root.findall("t:TestDefinitions/t:UnitTest", NS)
        entries = root.findall("t:TestEntries/t:TestEntry", NS)
        record["actual_result_names"] = [r.attrib.get("testName") for r in results]
        record["actual_outcomes"] = [r.attrib.get("outcome") for r in results]
        if len(results) != 7 or len(definitions) != 7 or len(entries) != 7:
            record["violations"].append("Exactly seven original results/definitions/entries required")
        if len(set(record["actual_result_names"])) != 7 or set(record["actual_result_names"]) != EXPECTED:
            record["violations"].append("Original seven result names differ or duplicate")
        definition_ids = [d.attrib.get("id") for d in definitions]
        result_ids = [r.attrib.get("testId") for r in results]
        executions = [r.attrib.get("executionId") for r in results]
        if any(not x for x in definition_ids + result_ids + executions) or len(set(definition_ids)) != 7 or len(set(result_ids)) != 7 or len(set(executions)) != 7:
            record["violations"].append("Unique actual test/execution identities absent")
        by_id = {d.attrib.get("id"): d for d in definitions}
        links = []
        for result in results:
            name = result.attrib.get("testName", "")
            definition = by_id.get(result.attrib.get("testId"))
            if definition is None or name not in EXPECTED:
                record["violations"].append("Original result/definition missing or unexpected")
                continue
            method = definition.find("t:TestMethod", NS)
            execution = definition.find("t:Execution", NS)
            cls, method_name = name.rsplit(".", 1)
            if result.attrib.get("outcome") != "Passed" or method is None or method.attrib.get("className") != cls or method.attrib.get("name") != method_name:
                record["violations"].append("Original method identity/outcome mismatch: " + name)
            matches = [e for e in entries if e.attrib.get("testId") == result.attrib.get("testId") and e.attrib.get("executionId") == result.attrib.get("executionId")]
            if len(matches) != 1 or execution is None or execution.attrib.get("id") != result.attrib.get("executionId"):
                record["violations"].append("Actual execution linkage invalid: " + name)
            links.append({"name": name, "testId": result.attrib.get("testId"),
                          "executionId": result.attrib.get("executionId"),
                          "method": method.attrib if method is not None else None})
        record["actual_identity_links"] = links
    except (ET.ParseError, ValueError, TypeError, OSError) as error:
        record["violations"].append(repr(error))
    record["accepted"] = not record["violations"]
    return record
