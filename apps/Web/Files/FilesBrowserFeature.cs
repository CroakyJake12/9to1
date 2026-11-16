using System.Runtime.Versioning;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Files;

/// <summary>Enrolls the existing Files surface only after the private server verifies the owner.</summary>
[SupportedOSPlatform("browser")]
public static class FilesBrowserFeature
{
    public static async Task<bool> RegisterAsync(BrowserSurfaceRegistry registry, Func<bool> isContextCurrent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(isContextCurrent);
        if (!isContextCurrent()) return false;
        var transport = new FilesBrowserTransport();
        var enrolled = false;
        try
        {
            var context = await transport.CallAsync<FilesDriveContext>("context", null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isContextCurrent()) return false;
            context.Validate();
            using var stream = typeof(FilesBrowserFeature).Assembly.GetManifestResourceStream("NineToOne.Web.Files.cui")
                ?? throw new InvalidOperationException("The Files CUI resource is unavailable.");
            using var reader = new StreamReader(stream);
            var document = new CuiDocumentSource(reader.ReadToEnd(), "Files.cui");
            var provider = new FilesDriveBrowserProvider(transport, context);
            var route = new FilesBrowserRoute(provider, context.AccountId, document.Document, transport.Revoke, transport.DrainAsync);
            transport.Enroll();
            var result = registry.Register(route, route.CreateSurface);
            if (!result.Succeeded)
            {
                route.Dispose();
                return false;
            }
            enrolled = true;
            return true;
        }
        catch (FilesBrowserRequestException error) when (error.Error.Code == HavenOS.Files.FilesErrorCode.PermissionDenied)
        {
            return false;
        }
        finally
        {
            if (!enrolled)
            {
                transport.Revoke();
                await transport.DrainAsync();
            }
        }
    }
}
