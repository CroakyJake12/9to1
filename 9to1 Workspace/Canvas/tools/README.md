# Audit Canvas donor capability and native artifacts

From the Canvas directory, use `python3 tools/inspect_rnote_capabilities.py --check` to compare the checked-in capability inventory with the controlled Rnote source. Run the same command without `--check` only when intentionally regenerating that inventory. The tool hashes inspected source and records the donor revision.

A detected action declaration is a candidate for parity work. Verify its state, action and result in the native Canvas owner before claiming support. Scope artifact-validation and Windows staging results to the exact binary and architecture examined. Keep original donor files intact.

[Inventory producer](inspect_rnote_capabilities.py) · [Validation tests](test_validate_rnote_native_artifact.py) · [Bridge provenance](../rnote-poc/PROVENANCE.md)
