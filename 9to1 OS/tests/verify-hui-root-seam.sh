#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
surface="$root/HUI/LinuxHost/HuiPreviewSurface.cs"
window="$root/HUI/LinuxHost/PreviewWindow.cs"
provider="$root/HUI/LinuxHost/HuiRootProvider.cs"
program="$root/HUI/LinuxHost/Program.cs"
app="$root/HUI/LinuxHost/App.cs"

grep -Fq 'public HuiPreviewSurface(HuiPage root)' "$surface"
grep -Fq 'new HavenInputRouter(_root)' "$surface"
grep -Fq 'public PreviewWindow(HuiPreviewSurface surface)' "$window"
grep -Fq 'Content = _surface' "$window"
grep -Fq 'HUI pointer + keyboard input passed' "$surface"
grep -Fq 'public interface IHuiRootProvider' "$provider"
grep -Fq 'HuiRootProviderAbi Abi { get; }' "$provider"
grep -Fq 'Page CreateRoot();' "$provider"
grep -Fq 'public const string AssemblyOption = "--hui-root-provider-assembly";' "$provider"
grep -Fq 'public const string TypeOption = "--hui-root-provider-type";' "$provider"
grep -Fq 'AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath))' "$provider"
grep -Fq 'HuiLinuxHostAbi.RequireCompatible(provider.Abi);' "$provider"
grep -Fq 'App.RootProvider = HuiRootProviderResolver.Resolve(args, out var avaloniaArgs);' "$program"
grep -Fq 'RootProvider.CreateRoot() ?? throw new InvalidOperationException("HUI root provider returned null.");' "$app"
grep -Fq 'new PreviewWindow(new HuiPreviewSurface(root))' "$app"
grep -Fq 'new PreviewWindow()' "$app"

echo "Generic HUI external root-provider, mounted root, and shared input seam contract passed."
