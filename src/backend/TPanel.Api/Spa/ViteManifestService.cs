using System.Text;
using System.Text.Json;

namespace TPanel.Api.Spa;

/// <summary>
/// Laravel'in @vite direktifinin yaptığını yapar: public/build/manifest.json okuyup
/// giriş noktası (resources/js/main.js) için &lt;script&gt;/&lt;link&gt; etiketlerini üretir.
/// SPA index HTML'i bu etiketlerle render edilir; frontend kaynağı değiştirilmez.
/// </summary>
public class ViteManifestService
{
    private const string Entry = "resources/js/main.js";
    private readonly string _publicPath;
    private readonly string _brand;
    private readonly object _lock = new();
    private Dictionary<string, ManifestChunk>? _manifest;
    private string? _cachedHtml;

    public ViteManifestService(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Frontend:PublicPath"] ?? "../../frontend/public";
        _publicPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        // Marka adı tek kaynaktan: App:Name. SPA bunu <meta app-brand>'den okur → tüm UI'a yansır.
        _brand = System.Net.WebUtility.HtmlEncode(config["App:Name"] ?? "PayDoPay");
    }

    public string PublicPath => _publicPath;

    /// <summary>SPA için tam HTML belgesi döner. Manifest yoksa açıklayıcı placeholder döner.</summary>
    public string RenderIndexHtml()
    {
        if (_cachedHtml is not null)
            return _cachedHtml;

        lock (_lock)
        {
            if (_cachedHtml is not null)
                return _cachedHtml;

            var manifestPath = Path.Combine(_publicPath, "build", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return BuildPlaceholder(manifestPath);
            }

            _manifest = JsonSerializer.Deserialize<Dictionary<string, ManifestChunk>>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new();

            _cachedHtml = BuildHtml();
            return _cachedHtml;
        }
    }

    private string BuildHtml()
    {
        var tags = new StringBuilder();
        var seenCss = new HashSet<string>();

        if (_manifest!.TryGetValue(Entry, out var entry))
        {
            // Giriş noktasının ve bağımlı parçaların CSS'leri
            CollectCss(entry, seenCss, tags);
            tags.Append($"<script type=\"module\" src=\"/build/{entry.File}\"></script>\n");
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <link rel="icon" href="/favicon.ico" />
              <meta name="robots" content="noindex, nofollow" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="app-brand" content="{{_brand}}" />
              <title>{{_brand}}</title>
              <link rel="stylesheet" type="text/css" href="/loader.css" />
            {{tags}}
            </head>
            <body>
              <div id="app"></div>
              <script>
                const loaderColor = localStorage.getItem('vuexy-initial-loader-bg') || '#FFFFFF'
                const primaryColor = localStorage.getItem('vuexy-initial-loader-color') || '#7367F0'
                if (loaderColor) document.documentElement.style.setProperty('--initial-loader-bg', loaderColor)
                if (primaryColor) document.documentElement.style.setProperty('--initial-loader-color', primaryColor)
              </script>
            </body>
            </html>
            """;
    }

    private void CollectCss(ManifestChunk chunk, HashSet<string> seen, StringBuilder tags)
    {
        if (chunk.Css is not null)
        {
            foreach (var css in chunk.Css)
            {
                if (seen.Add(css))
                    tags.Append($"<link rel=\"stylesheet\" href=\"/build/{css}\" />\n");
            }
        }

        if (chunk.Imports is not null)
        {
            foreach (var import in chunk.Imports)
            {
                if (_manifest!.TryGetValue(import, out var dep))
                    CollectCss(dep, seen, tags);
            }
        }
    }

    private string BuildPlaceholder(string manifestPath)
        => $$"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="UTF-8" /><title>{{_brand}}</title></head>
            <body style="font-family:sans-serif;padding:2rem">
              <h2>{{_brand}} API çalışıyor ✅</h2>
              <p>Frontend build bulunamadı:</p>
              <code>{{manifestPath}}</code>
              <p>Frontend'i derlemek için <code>src/frontend</code> içinde <code>pnpm install &amp;&amp; pnpm build</code> çalıştırın.</p>
              <p>API sağlık kontrolü: <a href="/api/v1/health">/api/v1/health</a></p>
            </body></html>
            """;

    private class ManifestChunk
    {
        public string File { get; set; } = string.Empty;
        public string[]? Css { get; set; }
        public string[]? Imports { get; set; }
    }
}
