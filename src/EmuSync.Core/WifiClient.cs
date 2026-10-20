using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace EmuSync.Core;

public record SaveDescriptor(SaveInfo Info,string[] Paths);
public sealed class WifiClient : ISyncPeer
{
    readonly HttpClient client;
    public WifiClient(Pairing pairing)
    {
        pairing=Pairing.Parse(pairing.Encode());
        // A short connect timeout makes an unreachable PC fail in seconds instead of waiting out the transfer timeouts.
        var handler=new SocketsHttpHandler { AllowAutoRedirect=false, UseProxy=false, ConnectTimeout=TimeSpan.FromSeconds(5) };
        handler.SslOptions.RemoteCertificateValidationCallback=(_,cert,_,_) => cert is not null &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()),Convert.FromHexString(pairing.Certificate));
        client=new HttpClient(handler) { BaseAddress=new Uri($"https://{pairing.Host}:{pairing.Port}/"),Timeout=TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.Add("X-EmuSync-Key",pairing.Key);
    }
    public async Task<List<Game>> Games()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response=await client.GetAsync("api/games",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Ensure(response);
        var bytes=await Bytes(response,32*1024*1024,timeout.Token);
        return JsonSerializer.Deserialize<List<Game>>(bytes,new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }
    public async Task<SaveDescriptor> Describe(string id)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response=await client.GetAsync($"api/saves/{SafePath.GameId(id)}/info",timeout.Token);
        Ensure(response);
        return await response.Content.ReadFromJsonAsync<SaveDescriptor>(timeout.Token) ?? throw new IOException("The PC returned invalid save information.");
    }
    public async Task<SaveBundle> DownloadSave(string id,string hash)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/saves/{SafePath.GameId(id)}");
        request.Headers.Add("X-Expected-Hash",hash);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Ensure(response);
        var bundle=SaveBundle.FromZip(await Bytes(response,SaveBundle.Limit,timeout.Token),id);
        if (bundle.Info.Hash!=hash) throw new IOException("The save failed verification. Nothing was replaced.");
        return bundle;
    }
    public async Task UploadSave(string id,SaveBundle bundle,string? expected)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var request=new HttpRequestMessage(HttpMethod.Put,$"api/saves/{SafePath.GameId(id)}");
        request.Headers.Add("X-Expected-Hash",expected??"none");
        request.Content=new ByteArrayContent(bundle.Zip());
        using var response=await client.SendAsync(request,timeout.Token);
        Ensure(response);
    }
    public async Task<(string Path,string Hash)> DownloadGame(Game game,string cache,Action<string> progress)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var response=await client.GetAsync($"api/games/{SafePath.GameId(game.Id)}/iso",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Ensure(response);
        var digest=response.Headers.TryGetValues("X-Content-SHA256",out var values)?values.Single():"";
        return await GameDownload.Receive(response,game,digest,cache,progress,timeout.Token);
    }
    static async Task<byte[]> Bytes(HttpResponseMessage response,int limit,CancellationToken token)
    {
        if (response.Content.Headers.ContentLength>limit) throw new IOException("The PC response is too large.");
        await using var input=await response.Content.ReadAsStreamAsync(token);
        using var output=new MemoryStream();
        await SaveBundle.CopyLimited(input,output,limit,token);
        return output.ToArray();
    }
    static void Ensure(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode) return;
        throw new IOException(response.StatusCode switch {
            HttpStatusCode.Unauthorized => "Pairing was reset on the PC. Paste a fresh pairing code in Settings.",
            HttpStatusCode.Conflict => "The PC save changed during sync. Nothing was overwritten. Press Play to check again.",
            HttpStatusCode.Locked => "PPSSPP is running on the PC. Close the game there, then try again.",
            _ => $"The PC could not complete this transfer ({(int)response.StatusCode}). Check the PC app and try again."
        });
    }
    public void Dispose()=>client.Dispose();
}
