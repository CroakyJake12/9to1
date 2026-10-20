// Root integration patch (extra12): REAL host-support admission for the genuine-V1 typed consumer.
//
// Insert this METHOD into `framework/CUI/Runtime/CuiRuntimeCompatibility.cs` inside the existing
// `public static class CuiRuntimeCompatibility`, immediately AFTER the V2
// `ValidateArtifactHostSupport(CuiCompiledArtifact, CuiSourceSpan)` method (the lane-2 Runtime
// patch). It reuses the same private helpers (RequireLanguage/RequireRegistry/RequireAbi/
// RequireCapabilities/IsAbiSupported) and constants (MinStatelessAbi/CurrentAbi) that the lane-2
// patch introduces, so it must be applied AFTER that patch.
//
// Preimage dependency: the lane-2 Runtime patch
//   `work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs`
// must already be applied (provides the V2 ValidateArtifactHostSupport, the private helpers and the
// MinStatelessAbi=1 / CurrentAbi=2 / RuntimeAbiVersion="2" constants). This addition only adds the
// genuine-V1 overload. No other file changes are required.

/*
    /// <summary>
    /// Admits a decoded genuine-V1 compiled artifact against the REAL host-support contract: this
    /// runtime's supported CUI language, component registry, minimum/current ABI range and capability
    /// set. Genuine stateless V1 documents carry minimum ABI1 and are valid on the stateful-aware
    /// runtime (current ABI2); a document or envelope declaring an ABI/capability outside the
    /// supported range fails before any reconstruction/effect.
    /// </summary>
    public static void ValidateArtifactHostSupport(CuiV1LoadedArtifact artifact, CuiSourceSpan diagnosticSpan)
    {
        if (artifact is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIB003", CuiDiagnosticSeverity.Error, "A compiled CUI artifact is required.", diagnosticSpan));
        if (artifact.Metadata is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIB003", CuiDiagnosticSeverity.Error,
                "The compiled CUI artifact is missing envelope compatibility metadata.", diagnosticSpan));

        // Envelope requirements (the conservative whole-bundle boundary): this is the admitted
        // contract for the entire required closure, so a sibling whose ABI/capability the host does
        // not support cannot escape whole-bundle checks.
        RequireLanguage(artifact.Metadata.LanguageVersion, diagnosticSpan);
        RequireRegistry(artifact.Metadata.ComponentRegistryVersion, diagnosticSpan);
        RequireAbi(artifact.Metadata.RuntimeAbiVersion, diagnosticSpan);
        RequireCapabilities(artifact.Metadata.RequiredCapabilities, diagnosticSpan);

        if (artifact.Documents is not null)
        {
            foreach (var document in artifact.Documents)
            {
                if (document?.Metadata is null) continue;
                RequireLanguage(document.Metadata.LanguageVersion, diagnosticSpan);
                RequireRegistry(document.Metadata.ComponentRegistryVersion, diagnosticSpan);
                RequireAbi(document.Metadata.RuntimeAbiVersion, diagnosticSpan);
                RequireCapabilities(document.Metadata.RequiredCapabilities, diagnosticSpan);
            }
        }
    }
*/
