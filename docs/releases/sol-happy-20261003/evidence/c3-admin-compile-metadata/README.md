# Genuine SDK post-target compiler-input proposal

Isolated tooling proposal6b3679bbe89c3b50b9ee00d7e96e63a19a4a3a6a adds actual SDK GenerateTargetFrameworkMonikerAttribute followed by GenerateAssemblyInfo before getItem:Compile. It does not run CoreCompile, substitute PDB documents into evaluated inputs, or waive the original nonempty compiler-input gate. Actual full driver attempt4 remains refused.

Three original physical projects were queried under exact original metadata contexts: Avalonia package, Accounts.Specs and Web.Specs. Original metadata reports0/4/2 inputs. Correct target order yields1/7/5 genuine SDK inputs; the package's sole input is the original actual PDB-bound AssemblyInfo source. All2480 original managed/task output files remain byte/SHA-identical. Existing physical PE/PDB identity and all actual source-document hashes for these three maintained projects were reverified.

An initial wrong-order query (AssemblyInfo before TFM attribute) created an uncompiled TFM attribute file and2package inputs. The actual output-closure comparison caught that one added file. Its original bytes, query logs and delta remain here; only that observed new mutable file was moved out of receiving artifacts before corrected replay. No PE/PDB changed. This draft is a negative control, not accepted compiler-input proof.

All9 bounded actual queries across before/draft/corrected stages exited0 with original strict session drains true. Five source-bound unittest controls pass, covering genuine original input refusal, corrected package PDB-bound source, all three contexts/source hashes, all2480 current output hashes and preserved wrong-order negative. Counts are this tooling cohort only.

Control command (cwd `/workspace/team-c/c3-admin-compile-metadata-proposal`):

```sh
ASTRA_METADATA_RECEIVING_ROOT=/workspace/team-c/c3-admin-generated-validation ASTRA_METADATA_EVIDENCE_ROOT=/workspace/team-c/evidence/c3/admin-sdk03 python3 .github/scripts/astra-compile-metadata-tests.py
```

No original Web/Release suite acceptance, product key adoption, real HTTPS issuer/provider/native or global release proof follows. Full owning rerun requires independent peer, complete receiving mapping and fresh root external pins. SDK10.0.401 remains a variance from workflow10.0.301.
