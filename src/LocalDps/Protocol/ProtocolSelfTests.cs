using System.IO;
namespace LocalDps.Protocol;
public static class ProtocolSelfTests { public static string Run() {


static byte[] V(int n) { var b=new List<byte>();do {byte x=(byte)(n&127);n>>=7;b.Add((byte)(x|(n>0?128:0)));}while(n>0);return b.ToArray(); }
static byte[] Hit(int counter) { var b=new List<byte>{4,0x38};b.AddRange(V(200));b.Add(4);b.Add(0);b.AddRange(V(100));b.AddRange(BitConverter.GetBytes(11010001));b.Add((byte)counter);b.Add(0);b.AddRange(new byte[8]);b.Add(0);b.AddRange(V(1234));b.Add(1);b.Add(0);var bytes=V(b.Count+4).Concat(b).ToArray();return bytes; }
var a=Hit(1);var d=new GameProtocolDecoder();if(d.Feed("a",a.AsSpan(0,7),0).Count!=0)throw new Exception("partial");var h=d.Feed("a",a.AsSpan(7),0);if(h.Count!=1||h[0].Damage!=1234)throw new Exception("direct");if(d.Feed("a",Hit(2),1).Count!=1)throw new Exception("equal separate hits");d.Reset();var all=a.Concat(Hit(2)).ToArray();if(d.Feed("a",all,0).Count!=2)throw new Exception("coalesced");
var literals=new List<byte>{0xf0};int len=a.Length-15;while(len>=255){literals.Add(255);len-=255;}literals.Add((byte)len);literals.AddRange(a);var body=new List<byte>{255,255};body.AddRange(BitConverter.GetBytes(a.Length));body.AddRange(literals);var bundle=V(literals.Count+10).Concat(body).ToArray();d.Reset();if(d.Feed("b",bundle,0).Count!=1||d.BundlesDecoded!=1)throw new Exception("bundle");
for(int i=0;i<a.Length;i++){ d.Reset();if(d.Feed("x",a.AsSpan(0,i),0).Count+d.Feed("x",a.AsSpan(i),0).Count!=1)throw new Exception("split "+i); }


// DoT: target varint, one-byte form, actor varint, instance varint, skill*100 LE32, damage varint.
var db=new List<byte>{5,0x38};db.AddRange(V(200));db.Add(0);db.AddRange(V(100));db.Add(1);db.AddRange(BitConverter.GetBytes(11010000*100));db.AddRange(V(567));var dot=V(db.Count+4).Concat(db).ToArray();d.Reset();var dh=d.Feed("dot",dot,0);if(dh.Count!=1||dh[0].Damage!=567||!dh[0].IsDot)throw new Exception("dot");
// Two-byte declared length verifies framing adjustment tracks varint width.
var large=Hit(3).Skip(1).Concat(new byte[110]).ToArray();var big=V(large.Length+4).Concat(large).ToArray();d.Reset();d.Framing=GameFraming.WidthAware;if(d.Feed("wide",big,0).Count!=1)throw new Exception("wide length");
// Legacy combat framing uses declared-3 regardless of length-varint width.
// Two coalesced large records prove the next boundary as well as the first hit.
d.Reset();d.Framing=GameFraming.LegacyCombat;
var legacy=V(large.Length+5).Concat(large).ToArray();
if(d.Feed("legacy",legacy.Concat(legacy).ToArray(),0).Count!=2)throw new Exception("legacy wide boundary");
var batchBody=new byte[]{0x3d,0x38}.Concat(Hit(4)).Concat(Hit(5)).ToArray();
var batch=V(batchBody.Length+4).Concat(batchBody).ToArray();d.Reset();
if(d.Feed("batch",batch,0).Count!=2)throw new Exception("batch container");


return "Protocol checks passed: damage, DoT, every TCP split, coalesced records, separate equal hits, LZ4, wide framing."; } }
