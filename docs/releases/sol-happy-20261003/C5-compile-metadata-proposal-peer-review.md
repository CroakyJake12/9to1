# C5 actual Compile metadata proposal review

C5 acknowledges bounded tooling source proposal `6b3679bbe89c3b50b9ee00d7e96e63a19a4a3a6a`, with no actionable regression found. Reviewed every changed line in two files, actual SDK10.0.401 target definitions and real query evidence. No global adoption or full SDK acceptance is implied.

Item queries run genuine SDK GenerateTargetFrameworkMonikerAttribute then GenerateAssemblyInfo before reading Compile. TFM generation is conditioned on existing Compile items: this order preserves the original package's sole AssemblyInfo input; reversing order adds an uncompiled TFM file. The actual target dependency definitions do not invoke CoreCompile. No fabricated Compile input, manually synthesized source or empty-input exemption was introduced. Original strict source/PDB, creator/session/disappearance and external-cut gates remain.

Independent controls: **5/5 passed, exit 0**. [Own raw log](/workspace/team-c/evidence/c5/compile-metadata-6b367-review.log), SHA256 `952b80df1ddd9366455e220b3f2b5f3b55ecb5a1634a17aff0cb9b729aac67dc`.

Independently rehashed **all2480 actual original output files**: before and corrected-fixed2 closures are byte/size/SHA identical. All three metadata contexts match. Package inputs change0→1 and exactly match its original PDB; Accounts4→7 exactly matches seven original PDB documents; Web2→5 has every Compile input PDB-bound, with the sixth PDB document belonging to genuine emitted ASP.NET source generation. Full physical symbol/source checks pass. Nine before/wrong-order/corrected actual queries exited0 with strict original-session drain seals true and no cleanup error.

The rejected target-order draft, its uncompiled TFM source and one-file delta remain retained as negative evidence. Original full driver attempt4 remains refused; no full rerun was performed. Root must issue a fresh exact external complete-cut pin before any successor. [Exact source/SDK/evidence hashes and scope](C5-compile-metadata-proposal-peer-review.json).
