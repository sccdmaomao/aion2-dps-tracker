using System.Net;
using System.Net.Sockets;
namespace LocalDps.Capture;

public static class GameFlowDirection
{
    public static bool IsServerToClient(string flow, IReadOnlySet<int> serverPorts)
    {
        int split=flow.IndexOf('>');
        if(split<0||!Endpoint(flow[..split],out var source,out int sourcePort)||!Endpoint(flow[(split+1)..],out var destination,out _))return false;
        if(serverPorts.Count>0)return serverPorts.Contains(sourcePort);
        // Offline recordings have no running game process. Accept only the
        // unambiguous public-server -> private-client direction in that case.
        return !Private(source!)&&Private(destination!);
    }
    static bool Endpoint(string text,out IPAddress? address,out int port)
    {
        address=null;port=0;int end=text.LastIndexOf("]:" ,StringComparison.Ordinal);
        return text.StartsWith('[')&&end>1&&IPAddress.TryParse(text[1..end],out address)&&int.TryParse(text[(end+2)..],out port)&&port is >0 and <=65535;
    }
    static bool Private(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address)||address.IsIPv6LinkLocal||address.IsIPv6SiteLocal)return true;
        var b=address.GetAddressBytes();
        return address.AddressFamily==AddressFamily.InterNetwork
            ? b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1] is >=16 and <=31||b[0]==169&&b[1]==254
            : (b[0]&0xfe)==0xfc;
    }
}
