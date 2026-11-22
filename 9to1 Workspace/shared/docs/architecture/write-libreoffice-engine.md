# Write and the LibreOfficeKit engine boundary

Write owns the editable document, its identity and the user’s save decision. LibreOfficeKit supplies permitted engine functionality behind a reviewed bridge. Distinguish a successful standalone engine call from admission of an engine artifact into the shipping native editor.

Keep the LibreOffice program directory, build identity, headers and runtime version together. Verify that the bridge opens the expected document, reports errors and releases the original handle. Reopen the saved result through the owning Write path before claiming format or application parity.

## LibreOfficeKit program path

The path passed to `lok_init_2` must be the directory that directly contains `libsofficeapp.so` or `libmergedlo.so`; LibreOfficeKit appends one of those filenames to the supplied path. Ubuntu packages place these libraries below `/usr/lib/libreoffice/program`, which is therefore the default `LibreOfficeProgramPath` used by this PoC. Do not pass `/usr/lib/libreoffice` unless that package layout changes and direct runtime evidence confirms it.

## Security boundary

The engine must fail closed. Enabling the feature is not enough to make it available: the Linux host, helper executable, LibreOffice program directory, and required native libraries must all be present. A future helper process must run with an isolated profile, minimal filesystem mounts, no network by default, and no unrestricted UNO-command escape hatch exposed to generated UI or model output.

`WriteEngineCommand` is therefore a semantic application request. Platform implementations must allow-list and validate commands before translating them to LibreOffice UNO commands.

## Accessibility

Rendered tiles are not sufficient accessibility evidence. The engine contract requires a semantic accessibility snapshot in addition to pixels. The native PoCs exercise LibreOfficeKit focused-paragraph and caret APIs, but a complete HUI accessibility bridge remains a release gate.

## Clean-Ubuntu validation

`.github/workflows/validate-write-libreoffice-poc.yml` provides a disposable GitHub-hosted Ubuntu validation path. Its first job installs the Writer/headless runtime plus LibreOfficeKit development headers, restores/builds the focused .NET project, runs the configuration tests, creates a minimal ODT fixture, executes both native probes, independently verifies the normal edit and bold-format persistence, and publishes standalone prebuilt probes.

Baseline packaging/runtime run: **GitHub Actions run `34156074150`**, source head `7daa8d5d45e0ad35dfa319ce44178bf3983f46f6`.

Latest semantic-command run: **GitHub Actions run `34158386834`**, exact semantic source head `8e439c7869d144547615520f2cd98e882b7f4b1a`.

Build-host evidence on Ubuntu 24.04 includes:

- focused Release .NET build: passed;
- focused configuration/readiness tests: 4 passed, 0 failed;
- both native C++ probes compiled with `-Wall -Wextra -Wpedantic -Werror`;
- LibreOfficeKit 24.2.7.2 initialisation and Writer document load: passed;
- document dimensions: positive and stable across reopen;
- 512x512x4 tile render: passed, 1,048,576 bytes;
- focused-paragraph/caret accessibility call path: executed;
- Writer `paste()` edit: accepted;
- ODT save, LOK reopen and rerender: passed;
- persisted normal edit marker: independently verified;
- `.uno:SelectAll` command-result callback: observed;
- selected text was read only after that completion callback and contained the semantic proof marker;
- `.uno:Bold` command-result callback: observed;
- a selection callback was observed;
- `.uno:Bold=true` state callback was observed;
- semantic ODT save occurred only after Bold completion;
- independent ODT inspection resolved the semantic proof marker to bold style `P1`;
- both jobs in run `34158386834` completed successfully.

This is **portable Linux runtime proof**, not 9to1OS image/VM acceptance proof.

## Runtime-only packaging proof

The workflow's second job runs only after the build/probe job succeeds. It downloads the already-built native probes into a fresh `ubuntu:24.04` container and installs only:

- `libreoffice-core-nogui` `4:24.2.7-0ubuntu0.24.04.6`;
- `libreoffice-writer-nogui` `4:24.2.7-0ubuntu0.24.04.6`.

The runtime gate explicitly requires `libreofficekit-dev`, `liblibreofficekitgtk`, `gir1.2-lokdocview-0.1`, `libreoffice-gtk3`, and installed `libgtk-*` packages to be absent. `ldd` for both prebuilt probes must also show no direct GTK, LOKDocView, or LibreOfficeKit-GTK dependency.

On semantic run `34158386834`, exact source head `8e439c7869d144547615520f2cd98e882b7f4b1a`, the runtime-only job passed with:

- no compiler or LibreOfficeKit development headers installed in the container;
- forbidden GTK/LOKDocView/development package checks passing;
- both prebuilt probes resolving only ordinary C/C++ runtime dependencies directly;
- 119 newly installed packages plus one base-image package upgrade;
- about 116 MB downloaded and about 366 MB additional disk usage;
- LibreOfficeKit 24.2.7.2 initialised from the headless runtime;
- baseline render/edit/save/reopen proof passed;
- `.uno:SelectAll` command-result callback observed;
- selection read after completion: 72 bytes, `text/plain;charset=utf-8`, containing the semantic marker;
- `.uno:Bold` command-result callback observed;
- selection callback observed;
- `Bold=true` state callback observed;
- semantic ODT saved after completed Bold;
- independent verification outside the container resolved the persisted semantic marker to bold style `P1`;
- the runtime-only job completed successfully.

This closes the tested PoC question of whether `libreofficekit-dev`, GTK, LOKDocView, or GNOME packages are required at runtime: **they are not required by the tested raw-LibreOfficeKit Writer slice, including callback-driven Select All/Bold semantics**. It does not prove that the future production helper/package image has no additional dependencies.

[Pinned native bridge](../../../Write/Native/README.md) · [Write host](../../../Write/README.md)
