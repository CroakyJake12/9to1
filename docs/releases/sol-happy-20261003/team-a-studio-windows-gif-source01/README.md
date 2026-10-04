Studio import and avatar preview now share an explicitly selected maintained Windows GIF decoder. Linux continues to use its existing glycin decoder. Unsupported formats fail before attachment writes.

This proposal contains twelve product files. Root and an independent reviewer checked the complete source, seven exact predecessors, preserved original assertions and the maintained decoder APIs. The fourteen authored native test cases remain unrun.

Use the exact source on this branch in an isolated checkout. Its parent is fd5b989bcaebbd44261f0d5e4d02f72d7381b4b8, the existing native validation baseline. The unchanged .github/workflows/team-c-windows-package-probe.yml uses Windows2022, global.json and .github/scripts/windows-package-probe.ps1. The unchanged prepare-cui-source.sh restores and verifies the exact XamlX, Avalonia.DBus and libvterm Gitlinks. SkiaSharp remains at the repository-selected3.119.3-preview.1.1.

Run the complete maintained Studio test project, including the original seven cases and the added decoder/picker controls, before package validation. Colour-profile coverage, other formats, resource isolation, installed Home compatibility, clean-PC acceptance and the expanded Pass1/LE requirements remain open.

Team C owns global integration. Its current global candidate42a88 lacks this native project closure; select the reviewed leaf changes and coherent dependencies instead of merging historical branch ancestry. Source review does not establish Windows package acceptance.
