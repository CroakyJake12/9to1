#!/usr/bin/env python3
"""Generate a local source-linked test harness, not an alternate production service.

Full-project tests remain required. This harness isolates the provider class from
unrelated application compilation defects. All domain dependencies are original
source or verbatim source excerpts; only test transport/configuration are fakes.
"""
import argparse
import hashlib
from pathlib import Path
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
parser.add_argument("--provider-source", type=Path, help="Isolated negative-control source; never edits the working provider")
parser.add_argument("--production-project", action="store_true", help="Reference the real Infrastructure project and full current owning test files")
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
shared = root / "9to1 Workspace/shared"
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)

if args.production_project:
    # This avoids the unrelated Browse/Avalonia build-task dependency of the full
    # test project; production Infrastructure and all of its own dependencies are
    # built normally. No production type or test assertion is extracted/replaced.
    tests = [shared / 'tests/Haven.Infrastructure.Tests' / name for name in [
        'CloudflareWorkersAiMetadataTests.cs', 'ProviderEndpointSecurityTests.cs']]
    includes = '<EmbeddedResource Include="' + escape(str(shared/'tests/Haven.Infrastructure.Tests/Fixtures/cloudflare-workers-ai-catalogue-20261004.json')) + '" />\n' + '\n'.join(f'<Compile Include="{escape(str(path))}" />' for path in tests)
    infrastructure = shared / 'src/Haven.Infrastructure/Haven.Infrastructure.csproj'
    (output/'C5.Provider.Tests.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AssemblyName>Haven.Infrastructure.Tests</AssemblyName><Platform>x64</Platform><PlatformTarget>x64</PlatformTarget></PropertyGroup>
      <ItemGroup>{includes}<Using Include="Xunit" />
      <ProjectReference Include="{escape(str(infrastructure))}" />
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
      <PackageReference Include="xunit" Version="2.9.3" />
      <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
      </ItemGroup></Project>''')
    for path in tests + [infrastructure, shared/'src/Haven.Infrastructure/Providers/CloudModelProviders.cs']:
        print(hashlib.sha256(path.read_bytes()).hexdigest(), path)
    raise SystemExit(0)

def excerpt(path, start, end):
    source = path.read_text()
    return source[source.index(start):source.index(end)]

abstractions = shared / "src/Haven.Application/Abstractions.cs"
requests = excerpt(abstractions, "public interface IOllamaClient", "public interface IWorkspaceToolService")
# Cut the XML documentation preceding the next declaration; it is not code.
requests = requests[:requests.rfind("/// <summary>")]
(output / "Requests.cs").write_text("using Haven.Core;\nnamespace Haven.Application;\n" + requests)

usage = shared / "src/Haven.Infrastructure/Persistence/SQLite/ModelUsageRepository.cs"
buffer = excerpt(usage, "public sealed class ProviderUsageCaptureBuffer", "public sealed class ProviderPricingService")
buffer = buffer[:buffer.rfind("/// <summary>")]
(output / "UsageBuffer.cs").write_text("using System.Collections.Concurrent;\nusing Haven.Application;\nusing Haven.Core;\nnamespace Haven.Infrastructure;\n" + buffer)
(output / "GlobalUsings.cs").write_text("global using Xunit;\n")

endpoint_path = shared / "tests/Haven.Infrastructure.Tests/ProviderEndpointSecurityTests.cs"

linked = [
    args.provider_source.resolve() if args.provider_source else shared / "src/Haven.Infrastructure/Providers/CloudModelProviders.cs",
    shared / "src/Haven.Infrastructure/Providers/ProviderToolTurnCorrelation.cs",
    shared / "src/Haven.Application/ModelProviderAbstractions.cs",
    shared / "src/Haven.Application/UsageAbstractions.cs",
    shared / "tests/Haven.Infrastructure.Tests/CloudflareWorkersAiMetadataTests.cs",
    endpoint_path,
]
items = '<EmbeddedResource Include="' + escape(str(shared/'tests/Haven.Infrastructure.Tests/Fixtures/cloudflare-workers-ai-catalogue-20261004.json')) + '" />\n' + "\n".join(f'<Compile Include="{escape(str(path))}" Link="{path.name}" />' for path in linked)
core = shared / "src/Haven.Core/Haven.Core.csproj"
(output / "C5.Provider.Tests.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>{items}
    <ProjectReference Include="{escape(str(core))}" />
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>''')
for path in linked + [abstractions, usage, endpoint_path]:
    print(hashlib.sha256(path.read_bytes()).hexdigest(), path)
