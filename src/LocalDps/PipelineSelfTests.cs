using System.Buffers.Binary;
using LocalDps.Capture;
using LocalDps.Protocol;
namespace LocalDps;

public static class PipelineSelfTests
{
    public static void Run()
    {
        // Exercise the actual PCAP reader -> Ethernet/TCP -> decoder -> meter chain.
        byte[] Var(int n){var b=new List<byte>();do{byte v=(byte)(n&127);n>>=7;b.Add((byte)(v|(n>0?128:0)));}while(n>0);return b.ToArray();}
        var body=new List<byte>{4,0x38};body.AddRange(Var(200));body.AddRange(new byte[]{4,0});body.AddRange(Var(100));body.AddRange(BitConverter.GetBytes(11010001));body.AddRange(new byte[]{1,0});body.AddRange(new byte[8]);body.Add(0);body.AddRange(Var(1234));body.AddRange(new byte[]{1,0});
        var chainedBody=body.Concat(new byte[]{1,0}).Concat(body.Skip(2)).ToArray();
        var record=Var(chainedBody.Length+4).Concat(chainedBody).ToArray();
        byte[] Frame(uint seq, ReadOnlySpan<byte> payload, byte flags=0x10)
        {
            var f=new byte[54+payload.Length];f[12]=8;f[14]=0x45;f[23]=6;
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(16),(ushort)(40+payload.Length));
            f[26]=10;f[29]=1;f[30]=10;f[33]=2;
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(34),1234);BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(36),4321);
            BinaryPrimitives.WriteUInt32BigEndian(f.AsSpan(38),seq);f[46]=0x50;f[47]=flags;payload.CopyTo(f.AsSpan(54));return f;
        }
        var path=Path.Combine(Path.GetTempPath(),"localdps-synthetic-"+Guid.NewGuid()+".pcap");
        try
        {
            using(var file=File.Create(path))using(var writer=new BinaryWriter(file))
            {
                writer.Write(0xa1b2c3d4u);writer.Write((ushort)2);writer.Write((ushort)4);writer.Write(0);writer.Write(0);writer.Write(65535);writer.Write(1);
                void Write(byte[] packet){writer.Write(1000u);writer.Write(0u);writer.Write(packet.Length);writer.Write(packet.Length);writer.Write(packet);}
                Write(Frame(99,Array.Empty<byte>(),2));
                Write(Frame(107,record.AsSpan(7))); // future segment first
                Write(Frame(100,record.AsSpan(0,7)));
                Write(Frame(100,record)); // retransmission must not duplicate damage
            }
            using var service=new PacketCaptureService();var decoder=new GameProtocolDecoder{IncludeHit=SkillRules.LoadBundled().Include};var meter=new Meter();
            service.StreamChunk+=(data,time,flow)=>{foreach(var hit in decoder.Feed(flow,data,time.ToUnixTimeMilliseconds()))meter.Add(hit);};
            service.ReplayPcap(path);
            if(meter.TotalHits!=2||meter.TotalDamage!=2468||service.PacketsReceived!=4)throw new Exception("Chained PCAP pipeline/retransmission totals failed");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        // Deterministic malformed-input smoke: packet and stream parsers must stay bounded and not throw.
        var random=new Random(477);var streams=new TcpStreamAssembler();var protocol=new GameProtocolDecoder();
        for(int i=0;i<1000;i++){var bytes=new byte[random.Next(0,300)];random.NextBytes(bytes);streams.ProcessFrame(bytes,DateTimeOffset.UnixEpoch,i%3==0?0:i%3==1?1:101);protocol.Feed("fuzz"+(i%7),bytes,0);}
    }
}
