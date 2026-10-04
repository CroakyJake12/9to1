# Studio Windows source declaration proposal

The published Studio/Picture raster-provider source at `c22a80f41fdc584d890e84356256a03e8e3951af` adds dependencies that the existing Windows validation catalog does not declare. [Run 37221887666](https://github.com/CroakyJake12/9to1/actions/runs/37221887666) restored the pinned donors and SDK, then all three jobs failed source preflight before compilation, tests or packaging:

`Undeclared source change: 9to1 Workspace/Picture/Core/HavenOS.Images.Core.csproj`

The adjacent `team-a-windows-gif-validation-catalog-proposal-20261004.json` is a complete proposed replacement for `.github/validation/windows-package-probe.json` on that exact native source branch. It declares the existing fourteen source/review files, pins all twelve actual product/fixture bodies, preserves historical provenance, and selects all fourteen authored cases from the three Studio fixture classes. The validation script, workflow, original source basis, ancestry checks, unaffected pins, required files and other targets remain unchanged.

Team C owns the validation route and should review and apply this bounded catalog change on the exact source branch or a coherently selected successor. Copy the catalog file; do not merge coordination-branch ancestry. Retain the original failed run and run the corrected affected controls after source selection. No test, package, installed-Home or full-acceptance success is claimed by this source proposal.
