using EmuSync.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EmuSync.App;

public sealed class WifiHost : IAsyncDisposable
{
    WebApplication? app;
    readonly SemaphoreSlim gate=new(1,1);
    public string? CertificateHash { get; private set; }
    public string? Key { get; private set; }
    public const int Port=42387;
    // A game's SHA-256 is needed before its download can start. Remember it per scanned Game object, so a
    // library refresh (which creates new objects) recomputes it, and start hashing before anyone presses Play.
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Game,Task<string>> hashes=new();
    Task<string> GameHash(IGameFolder files,Game game)=>hashes.GetValue(game,g=>Task.Run(async()=> {
        try
        {
            await using var stream=await files.Read(g.FileName);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer=new byte[1<<20]; int count;
            while((count=await stream.ReadAsync(buffer))>0) hash.AppendData(buffer,0,count);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch { hashes.Remove(g); throw; }
    }));
    void WarmHashes(IGameFolder files,IReadOnlyList<Game> list)=>_=Task.Run(async()=> {
        foreach(var game in list) { try { await GameHash(files,game); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { } }
    });
    public async Task<bool> PreparePlay(Func<Task<bool>> prepare)
    {
        await gate.WaitAsync();
        try { return await prepare(); } finally { gate.Release(); }
    }
    public async Task Start(IGameFolder saves,IGameFolder files,Func<IReadOnlyList<Game>> games,Func<bool> busy,string data)
    {
        if(app is not null) return;
        Directory.CreateDirectory(data);
        var certificatePath=Path.Combine(data,"host.pfx");
        X509Certificate2 cert;
        if(File.Exists(certificatePath)) cert=X509CertificateLoader.LoadPkcs12FromFile(certificatePath,null);
        else
        {
            using var rsa=RSA.Create(2048);
            var request=new CertificateRequest("CN=EmuSync local pairing",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(3));
            await File.WriteAllBytesAsync(certificatePath,generated.Export(X509ContentType.Pkcs12));
            cert=X509CertificateLoader.LoadPkcs12FromFile(certificatePath,null);
        }
        CertificateHash=Convert.ToHexString(SHA256.HashData(cert.RawData));
        var keyPath=Path.Combine(data,"pairing.key");
        Key=File.Exists(keyPath)?await File.ReadAllTextAsync(keyPath):Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(keyPath,Key);
        var builder=WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options=> {
            options.Limits.MaxRequestBodySize=SaveBundle.Limit;
            options.ListenAnyIP(Port,listen=>listen.UseHttps(cert));
        });
        var host=builder.Build();
        host.Use(async(context,next)=> {
            var sent=context.Request.Headers["X-EmuSync-Key"].ToString();
            if(sent.Length!=64 || !sent.All(Uri.IsHexDigit) || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(sent),Convert.FromHexString(Key)))
            { context.Response.StatusCode=401; return; }
            if(context.Request.Method is not ("GET" or "PUT")) { context.Response.StatusCode=405; return; }
            try { await next(context); }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { if(!context.Response.HasStarted) { context.Response.StatusCode=400; await context.Response.WriteAsync("Transfer failed. Check the PC app."); } }
        });
        host.MapGet("/api/games",()=> { var list=games(); WarmHashes(files,list); return list; });
        host.MapGet("/api/games/{id}/iso",async(string id,HttpContext context)=> {
            var game=games().SingleOrDefault(g=>g.Id==SafePath.GameId(id));
            if(game is null) { context.Response.StatusCode=404; return; }
            var hash=await GameHash(files,game).WaitAsync(context.RequestAborted);
            await using var stream=await files.Read(game.FileName);
            context.Response.ContentLength=stream.Length;
            context.Response.Headers["X-Content-SHA256"]=hash;
            context.Response.ContentType="application/octet-stream";
            await stream.CopyToAsync(context.Response.Body,1<<20,context.RequestAborted);
        });
        host.MapGet("/api/saves/{id}/info",async(string id,HttpContext context)=> {
            await gate.WaitAsync(context.RequestAborted);
            try {
                if(busy()) { context.Response.StatusCode=423; return; }
                RequireGame(id,games());
                var bundle=await SaveBundle.Capture(saves,id);
                await context.Response.WriteAsJsonAsync(new SaveDescriptor(bundle.Info,bundle.Files.Keys.ToArray()));
            } finally { gate.Release(); }
        });
        host.MapGet("/api/saves/{id}",async(string id,HttpContext context)=> {
            await gate.WaitAsync(context.RequestAborted);
            try {
                if(busy()) { context.Response.StatusCode=423; return; }
                RequireGame(id,games());
                var bundle=await SaveBundle.Capture(saves,id);
                if(bundle.Info.Hash!=context.Request.Headers["X-Expected-Hash"].ToString()) { context.Response.StatusCode=409; return; }
                var bytes=bundle.Zip(); context.Response.ContentLength=bytes.Length;
                context.Response.ContentType="application/zip"; await context.Response.Body.WriteAsync(bytes);
            } finally { gate.Release(); }
        });
        host.MapPut("/api/saves/{id}",async(string id,HttpContext context)=> {
            using var input=new MemoryStream();
            await SaveBundle.CopyLimited(context.Request.Body,input,SaveBundle.Limit);
            var incoming=SaveBundle.FromZip(input.ToArray(),id);
            if(incoming.Info.Hash is null || incoming.Info.Suspicious) throw new InvalidDataException("Empty saves are not accepted.");
            await gate.WaitAsync(context.RequestAborted);
            try {
                if(busy()) { context.Response.StatusCode=423; return; }
                RequireGame(id,games());
                var current=await SaveBundle.Capture(saves,id);
                if((current.Info.Hash??"none")!=context.Request.Headers["X-Expected-Hash"].ToString()) { context.Response.StatusCode=409; return; }
                await SaveBundle.Apply(saves,id,incoming,Path.Combine(data,"backups"));
                context.Response.StatusCode=204;
            } finally { gate.Release(); }
        });
        try { await host.StartAsync(); app=host; }
        catch { await host.DisposeAsync(); throw; }
    }
    static void RequireGame(string id,IReadOnlyList<Game> games)
    { if(!games.Any(g=>g.Id==SafePath.GameId(id))) throw new InvalidDataException("Unknown game."); }
    public async ValueTask DisposeAsync() { if(app is not null) { await app.DisposeAsync(); app=null; } }
}
