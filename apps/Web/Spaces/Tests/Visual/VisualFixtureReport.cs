using System.Net;
using System.Security.Cryptography;
using System.Text;

/// <summary>Test-only delivery wrapper. It never changes captured pixels or adds
/// fixture values to the product. Raw PNGs remain internal comparison evidence.</summary>
internal static class VisualFixtureReport
{
    internal static string Write(string directory, int width, bool layoutChecksPassed)
    {
        var main = Path.Combine(directory, $"actual-browser-chat-restaurant-{width}x946.png");
        if (!File.Exists(main)) throw new FileNotFoundException("The original automated fixture capture is missing.", main);
        var captures = new[]
        {
            (Name: "Resting product layout with synthetic test input", Path: main),
            (Name: "Pointer interaction — synthetic test input", Path: Path.Combine(directory, $"actual-browser-chat-actions-hover-{width}x946.png")),
            (Name: "Keyboard interaction — synthetic test input", Path: Path.Combine(directory, $"actual-browser-chat-actions-focused-{width}x946.png"))
        };
        var html = new StringBuilder("""
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<title>VISUAL TEST FIXTURE — NOT LIVE ACCOUNT OR MODEL OUTPUT</title>
<style>
body{margin:0;background:#202126;color:#f5f5f7;font:16px system-ui,sans-serif;line-height:1.5}
header{position:sticky;top:0;z-index:1000;background:#fff19c;color:#17130a;border-bottom:6px solid #17130a;padding:20px 28px}
header strong{font-size:30px;letter-spacing:.03em}header b{font-size:22px}header p{margin:8px 0 0}
main{padding:24px}section{margin:26px 0;border:2px solid #e6d28b}h1{font-size:24px}h2,.meta{margin:14px 18px}
.pixels{overflow:auto;border-top:2px solid #e6d28b}.pixels img{display:block;max-width:none;height:auto}
.meta{overflow-wrap:anywhere;color:#ddd}code{font-size:14px}@media print{header{position:static;print-color-adjust:exact}section{break-inside:avoid}}
</style></head><body>
<header role="note" aria-label="Visual test fixture, not live account or model output">
<strong>VISUAL TEST FIXTURE</strong><br><b>NOT LIVE ACCOUNT OR MODEL OUTPUT</b>
<p>All restaurant messages, model names, recent chats, draft and progress are deterministic automated-test inputs.
No actual account, provider response, tool execution or persisted user data is shown. This is not functional acceptance.</p>
</header><main><h1>Isolated automated visual comparison</h1>
<p>The banner belongs to this report, outside the original product image. Captured PNG bytes and exact comparison dimensions are unchanged.
Raw PNGs are internal test evidence and must not be delivered as screenshots of a real account.</p>
""");
        html.Append("<p>Automated layout checks: <strong>")
            .Append(layoutChecksPassed ? "PASSED IN THIS FIXTURE" : "FAILED IN THIS FIXTURE")
            .Append("</strong>. Real-product functionality and full visual acceptance remain separate, unproven gates.</p>");
        foreach (var capture in captures.Where(capture => File.Exists(capture.Path)))
        {
            var bytes = File.ReadAllBytes(capture.Path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            html.Append("<section><h2>VISUAL TEST FIXTURE — ").Append(WebUtility.HtmlEncode(capture.Name))
                .Append("</h2><p class=\"meta\">NOT LIVE ACCOUNT OR MODEL OUTPUT · Original PNG SHA-256: <code>")
                .Append(hash).Append("</code></p><div class=\"pixels\"><img alt=\"Synthetic automated visual-test fixture, not live account or model output\" width=\"")
                .Append(width).Append("\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(bytes))
                .Append("\"></div><p class=\"meta\">Only layout and original-control interaction are exercised here. No model or tool ran.</p></section>");
        }
        html.Append("</main></body></html>");
        var report = Path.Combine(directory, "VISUAL-TEST-FIXTURE-NOT-LIVE.html");
        File.WriteAllText(report, html.ToString());
        return report;
    }
}
