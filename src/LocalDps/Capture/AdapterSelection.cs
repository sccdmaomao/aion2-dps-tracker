using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace LocalDps.Capture;

public sealed record AdapterHint(string Id,bool IsUp,bool HasGateway,NetworkInterfaceType Type);
public static class AdapterSelection
{
    public static CaptureDevice? ForGame(IReadOnlyList<CaptureDevice> devices,IReadOnlyList<GameConnection> connections)
    {
        foreach(var connection in connections.OrderByDescending(c=>c.RemotePort==13328))
        {
            var address=System.Net.IPAddress.Parse(connection.RemoteAddress);bool ipv6=address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6;
            byte[] socketAddress=new byte[ipv6?28:16];socketAddress[0]=(byte)(ipv6?23:2);address.GetAddressBytes().CopyTo(socketAddress,ipv6?8:4);
            if(GetBestInterfaceEx(socketAddress,out uint index)!=0)continue;
            foreach(var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    var properties=n.GetIPProperties();int? candidate=ipv6?properties.GetIPv6Properties()?.Index:properties.GetIPv4Properties()?.Index;
                    if(candidate!=index)continue;
                    var match=devices.FirstOrDefault(d=>d.Name.EndsWith("{"+n.Id.Trim('{','}')+"}",StringComparison.OrdinalIgnoreCase));if(match is not null)return match;
                }catch(NetworkInformationException){}
            }
        }
        return Recommend(devices);
    }
    [DllImport("iphlpapi.dll")]static extern uint GetBestInterfaceEx(byte[] destination,out uint index);
    public static CaptureDevice? Recommend(IReadOnlyList<CaptureDevice> devices)=>Recommend(devices,NetworkInterface.GetAllNetworkInterfaces().Select(n=>
    {
        bool gateway=false;try{gateway=n.GetIPProperties().GatewayAddresses.Any(g=>!g.Address.Equals(System.Net.IPAddress.Any)&&!g.Address.Equals(System.Net.IPAddress.IPv6Any));}catch(NetworkInformationException){}
        return new AdapterHint(n.Id,n.OperationalStatus==OperationalStatus.Up,gateway,n.NetworkInterfaceType);
    }));
    public static CaptureDevice? Recommend(IReadOnlyList<CaptureDevice> devices,IEnumerable<AdapterHint> hints)
    {
        var active=hints.Where(h=>h.IsUp&&h.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToArray();
        return devices.Select(d=>new{Device=d,Hint=active.FirstOrDefault(h=>d.Name.EndsWith("{"+h.Id.Trim('{','}')+"}",StringComparison.OrdinalIgnoreCase))})
            .Where(x=>x.Hint is not null).OrderByDescending(x=>x.Hint!.HasGateway).ThenByDescending(x=>x.Hint!.Type==NetworkInterfaceType.Wireless80211||x.Hint.Type==NetworkInterfaceType.Ethernet).Select(x=>x.Device).FirstOrDefault();
    }
    public static void SelfTest()
    {
        var wan=new CaptureDevice(@"\Device\NPF_{11111111-1111-1111-1111-111111111111}","WAN miniport");
        var wifi=new CaptureDevice(@"\Device\NPF_{22222222-2222-2222-2222-222222222222}","Wi-Fi");
        var hints=new[]{new AdapterHint("11111111-1111-1111-1111-111111111111",false,false,NetworkInterfaceType.Ethernet),new AdapterHint("{22222222-2222-2222-2222-222222222222}",true,true,NetworkInterfaceType.Wireless80211)};
        if(Recommend(new[]{wan,wifi},hints)!=wifi)throw new Exception("Active gateway adapter must win over first Npcap entry");
        if(Recommend(new[]{wan},hints)!=null)throw new Exception("Inactive-only inventory must not silently default to first entry");
    }
}
