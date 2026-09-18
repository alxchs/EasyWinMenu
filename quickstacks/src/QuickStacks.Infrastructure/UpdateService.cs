using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Implementação do serviço de atualização do QuickStacks com suporte a feed JSON em domínio privado.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;

    public const string DefaultPrivateFeedUrl = "https://updates.alxchs.com/quickstacks/version.json";

    public string DefaultFeedUrl { get; }

    public UpdateService(HttpClient? httpClient = null, string? currentVersion = null, string? defaultFeedUrl = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _currentVersion = currentVersion ?? ResolveAssemblyVersion();
        DefaultFeedUrl = defaultFeedUrl ?? DefaultPrivateFeedUrl;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string? feedUrl = null, CancellationToken cancellationToken = default)
    {
        var targetUrl = !string.IsNullOrWhiteSpace(feedUrl) ? feedUrl : DefaultFeedUrl;

        try
        {
            var response = await _httpClient.GetAsync(targetUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure(_currentVersion, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<UpdateManifestDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (dto is null || string.IsNullOrWhiteSpace(dto.Version))
            {
                return UpdateCheckResult.Failure(_currentVersion, "Manifesto de atualização inválido ou vazio.");
            }

            var updateInfo = new UpdateInfo(
                dto.Version.Trim(),
                dto.DownloadUrl?.Trim() ?? string.Empty,
                dto.Changelog?.Trim(),
                dto.Sha256?.Trim(),
                dto.IsMandatory,
                dto.ReleaseDateUtc
            );

            var comparison = CompareVersions(updateInfo.Version, _currentVersion);
            var hasUpdate = comparison > 0;

            return UpdateCheckResult.Success(hasUpdate, _currentVersion, updateInfo);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failure(_currentVersion, ex.Message);
        }
    }

    /// <summary>
    /// Compara duas versões no padrão SemVer (ex: "1.1.0" vs "1.0.5"). Retorna > 0 se remote for mais recente.
    /// </summary>
    public int CompareVersions(string remoteVersion, string localVersion)
    {
        var cleanRemote = CleanVersionString(remoteVersion);
        var cleanLocal = CleanVersionString(localVersion);

        if (Version.TryParse(cleanRemote, out var remoteVer) && Version.TryParse(cleanLocal, out var localVer))
        {
            return remoteVer.CompareTo(localVer);
        }

        var remoteParts = cleanRemote.Split('.');
        var localParts = cleanLocal.Split('.');
        var maxLen = Math.Max(remoteParts.Length, localParts.Length);

        for (var i = 0; i < maxLen; i++)
        {
            var rPart = i < remoteParts.Length && int.TryParse(remoteParts[i], out var rVal) ? rVal : 0;
            var lPart = i < localParts.Length && int.TryParse(localParts[i], out var lVal) ? lVal : 0;

            if (rPart != lPart)
            {
                return rPart.CompareTo(lPart);
            }
        }

        return 0;
    }

    private static string CleanVersionString(string version)
    {
        var trimmed = version.Trim().TrimStart('v', 'V');
        var dashIdx = trimmed.IndexOf('-');
        return dashIdx >= 0 ? trimmed[..dashIdx] : trimmed;
    }

    private static string ResolveAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIdx = infoVer.IndexOf('+');
            return plusIdx >= 0 ? infoVer[..plusIdx] : infoVer;
        }

        var ver = assembly.GetName().Version;
        return ver is not null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.1.0";
    }

    public sealed class UpdateManifestDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("changelog")]
        public string? Changelog { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; set; }

        [JsonPropertyName("releaseDateUtc")]
        public DateTime? ReleaseDateUtc { get; set; }
    }
}

