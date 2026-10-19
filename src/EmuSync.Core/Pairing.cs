using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuSync.Core;

public record Pairing(string Host,int Port,string Certificate,string Key)
{
    public string Encode() => "ES1-"+Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static Pairing Parse(string code)
    {
        try
        {
            if (code.Length>2048 || !code.StartsWith("ES1-")) throw new FormatException();
            var text=code[4..].Trim().Replace('-','+').Replace('_','/');
            var result=JsonSerializer.Deserialize<Pairing>(Convert.FromBase64String(text.PadRight((text.Length+3)/4*4,'='))) ?? throw new FormatException();
            if (!IPAddress.TryParse(result.Host,out var address) || address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork || result.Port is <1024 or >65535 ||
                !Regex.IsMatch(result.Certificate??"","^[A-F0-9]{64}$") || !Regex.IsMatch(result.Key??"","^[A-F0-9]{64}$")) throw new FormatException();
            var b=address.GetAddressBytes();
            if (!(b[0]==10 || b[0]==192 && b[1]==168 || b[0]==172 && b[1] is >=16 and <=31 || IPAddress.IsLoopback(address))) throw new FormatException();
            return result;
        }
        catch (Exception e) when(e is FormatException or JsonException or ArgumentException)
        { throw new FormatException("That pairing code is invalid. Copy a fresh code from your PC.",e); }
    }
}
