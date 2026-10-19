using System.Net.Http.Headers;

namespace LXIUM.Models;

/// <summary>공용 HTTP: GitHub API가 요구하는 User-Agent, 진행률을 알리는 받기</summary>
public static class Net
{
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LXIUM", Updater.Current));
        return http;
    }

    /// <summary>url을 file로 받습니다. progress에는 0~1 (크기를 모르면 알리지 않음)</summary>
    public static async Task DownloadAsync(string url, string file, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);
        var buffer = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total > 0) progress?.Report((double)done / total.Value);
        }
    }
}
