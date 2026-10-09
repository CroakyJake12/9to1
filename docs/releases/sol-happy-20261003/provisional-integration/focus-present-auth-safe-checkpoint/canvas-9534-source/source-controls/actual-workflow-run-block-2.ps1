& .github/scripts/canvas-packaged-ui-controls.ps1 -PackageInput '${{ runner.temp }}/canvas-package-input' -ObservationInput '${{ runner.temp }}/canvas-observation-input' -OutputDirectory $env:PROBE_OUTPUT
exit $LASTEXITCODE
