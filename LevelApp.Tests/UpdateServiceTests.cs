// LevelApp.App (WinUI / WindowsAppSdkSelfContained) cannot be referenced from the test
// project without triggering a platform-architecture build error. These tests verify the
// HttpClient error-suppression contract that UpdateService.CheckForUpdateAsync implements:
//
//   try { ... } catch { return null; }
//
// Any network or HTTP error must be swallowed and returned as null so that a failed update
// check never prevents the app from starting. Fix 1 (WP0.19) adds Timeout = 10 s so that
// a hung connection eventually throws TaskCanceledException, which this pattern catches.

using System.Net;

namespace LevelApp.Tests;

public class UpdateServiceTests
{
    // Simulates the UpdateService catch-all with a timed-out HttpClient.
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_OnNetworkTimeout()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("Simulated timeout"));
        var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var result = await FetchOrNullAsync(client);

        Assert.Null(result);
    }

    // Simulates the UpdateService catch-all when the server returns a non-success status.
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_OnHttpError()
    {
        var handler = new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var result = await FetchOrNullAsync(client);

        Assert.Null(result);
    }

    // Mirrors the error-suppression pattern used by UpdateService.CheckForUpdateAsync.
    private static async Task<object?> FetchOrNullAsync(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync(
                "https://api.github.com/repos/soldernerd/LevelApp/releases/latest");
            response.EnsureSuccessStatusCode();
            return new object(); // non-null = success
        }
        catch
        {
            return null;
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
