using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
namespace LocalDps.Capture;

public sealed record GameConnection(int LocalPort,int RemotePort,string RemoteAddress);
public static class GameConnectionDetection
{
    public static IReadOnlyList<GameConnection> Find()
    {
        var ids=new HashSet<int>();foreach(var p in Process.GetProcessesByName("AION2")){using(p)ids.Add(p.Id);}
        if(ids.Count==0)throw new InvalidOperationException("Open Aion 2 and enter the game, then click Start capturing.");
        var found=new List<GameConnection>();
        foreach(int family in new[]{2,23})
        {
            int size=0;uint status=GetExtendedTcpTable(IntPtr.Zero,ref size,false,family,5,0);
            if(status!=122&&status!=0)continue;
            if(size<4||size>32*1024*1024)continue;
            IntPtr table=Marshal.AllocHGlobal(size);
            try
            {
                status=GetExtendedTcpTable(table,ref size,false,family,5,0);if(status!=0)continue;
                int count=Marshal.ReadInt32(table),stride=family==2?24:56;
                if(count<0||count>(size-4)/stride)throw new InvalidDataException("Invalid Windows connection table.");
                for(int i=0;i<count;i++)
                {
                    IntPtr row=IntPtr.Add(table,4+i*stride);
                    int state=Marshal.ReadInt32(row,family==2?0:48),pid=Marshal.ReadInt32(row,family==2?20:52);
                    if(state!=5||!ids.Contains(pid))continue;
                    int ReadPort(int offset)=>(Marshal.ReadByte(row,offset)<<8)|Marshal.ReadByte(row,offset+1);
                    int local=ReadPort(family==2?8:20),remote=ReadPort(family==2?16:44);
                    byte[] address=new byte[family==2?4:16];Marshal.Copy(IntPtr.Add(row,family==2?12:24),address,0,address.Length);
                    var ip=new IPAddress(address);
                    if(local==0||remote==0||remote is 443 or 80 or 53||IPAddress.IsLoopback(ip))continue;
                    found.Add(new(local,remote,ip.ToString()));
                }
            }
            finally{Marshal.FreeHGlobal(table);}
        }
        if(found.Count==0)throw new InvalidOperationException("Aion 2 is running, but no supported game connection is active yet. Enter the world, then try again.");
        return found.Distinct().ToArray();
    }
    [DllImport("iphlpapi.dll",SetLastError=true)]static extern uint GetExtendedTcpTable(IntPtr table,ref int size,[MarshalAs(UnmanagedType.Bool)]bool order,int family,int tableClass,uint reserved);
}
