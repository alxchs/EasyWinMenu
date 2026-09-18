using System.Net;
using System.Net.Http;
using System.Text;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.0.5", "1.1.0", -1)]
    [InlineData("1.1.0", "1.1.0", 0)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.1.1", "1.1.0", 1)]
    [InlineData("1.1.0-rc1", "1.0.0", 1)]
    [InlineData("1.1.0.1", "1.1.0.0", 1)]
    public void CompareVersions_EvaluatesCorrectly(string remote, string local, int expectedSign)
    {
        var service = new UpdateService(currentVersion: local);
        var result = service.CompareVersions(remote, local);

        if (expectedSign > 0)
        {
            Assert.True(result > 0, $"Expected {remote} > {local}");
        }
        else if (expectedSign < 0)
        {
            Assert.True(result < 0, $"Expected {remote} < {local}");
        }
        else
        {
            Assert.Equal(0, result);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerVersionAvailable_ReturnsHasUpdateTrue()
    {
        var json = """
        {
            "version": "1.2.0",
            "downloadUrl": "https://updates.alxchs.com/quickstacks/QuickStacks-Setup-v1.2.0-x64.exe",
            "changelog": "Adicionado suporte a auto-atualizacao.",
            "sha256": "abcdef123456",
            "isMandatory": false,
            "releaseDateUtc": "2026-09-18T12:00:00Z"
        }
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var service = new UpdateService(httpClient, currentVersion: "1.1.0");

        var result = await service.CheckForUpdatesAsync("https://updates.alxchs.com/version.json");

        Assert.True(result.HasUpdate);
        Assert.Equal("1.1.0", result.CurrentVersion);
        Assert.NotNull(result.UpdateInfo);
        Assert.Equal("1.2.0", result.UpdateInfo.Version);
        Assert.Equal("https://updates.alxchs.com/quickstacks/QuickStacks-Setup-v1.2.0-x64.exe", result.UpdateInfo.DownloadUrl);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenSameVersion_ReturnsHasUpdateFalse()
    {
        var json = """
        {
            "version": "1.1.0",
            "downloadUrl": "https://updates.alxchs.com/quickstacks/QuickStacks-Setup-v1.1.0-x64.exe"
        }
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var service = new UpdateService(httpClient, currentVersion: "1.1.0");

        var result = await service.CheckForUpdatesAsync("https://updates.alxchs.com/version.json");

        Assert.False(result.HasUpdate);
        Assert.Equal("1.1.0", result.CurrentVersion);
        Assert.NotNull(result.UpdateInfo);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenHttpError_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "Not Found");
        var httpClient = new HttpClient(handler);
        var service = new UpdateService(httpClient, currentVersion: "1.1.0");

        var result = await service.CheckForUpdatesAsync("https://updates.alxchs.com/version.json");

        Assert.False(result.HasUpdate);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("404", result.ErrorMessage);
    }

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
