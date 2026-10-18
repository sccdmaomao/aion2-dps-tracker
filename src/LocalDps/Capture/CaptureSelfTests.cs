using System.Buffers.Binary;
using System.Text;

namespace LocalDps.Capture;

public static class CaptureSelfTests
{
    public static void Run()
    {
        var assembler = new TcpStreamAssembler();
        var chunks = new List<string>();
        assembler.StreamChunk += (bytes, _, _) => chunks.Add(Encoding.ASCII.GetString(bytes));
        var now = DateTimeOffset.UtcNow;
        assembler.ProcessFrame(Frame(99, "", 2), now);
        assembler.ProcessFrame(Frame(103, "def"), now);
        Require(chunks.Count == 0, "Out-of-order segment emitted before missing prefix");
        assembler.ProcessFrame(Frame(100, "abc"), now);
        Require(string.Concat(chunks) == "abcdef", "Out-of-order reconstruction");
        assembler.ProcessFrame(Frame(100, "abc"), now);
        Require(string.Concat(chunks) == "abcdef", "Retransmission duplicated damage bytes");
        assembler.ProcessFrame(Frame(104, "efghi"), now);
        Require(string.Concat(chunks) == "abcdefghi", "Partial retransmission overlap");
        assembler.Reset(); chunks.Clear();
        assembler.ProcessFrame(Frame(uint.MaxValue - 2, "", 2), now);
        assembler.ProcessFrame(Frame(uint.MaxValue - 1, "abcd"), now);
        assembler.ProcessFrame(Frame(2, "ef"), now);
        Require(string.Concat(chunks) == "abcdef", "TCP sequence wrap");
        assembler.Reset(); chunks.Clear();
        // Midstream captures start at their first observed payload.
        assembler.ProcessFrame(Frame(400, "hello"), now);
        Require(string.Concat(chunks) == "hello", "Midstream capture");
        assembler.ProcessFrame(new byte[2], now);
        var fragmented = Frame(405, "bad"); fragmented[20] = 0x20;
        assembler.ProcessFrame(fragmented, now);
        Require(string.Concat(chunks) == "hello", "IP fragment should not enter TCP reassembly");
        assembler.Reset(); chunks.Clear();
        var ipv6 = new byte[14 + 40 + 20 + 2]; ipv6[12] = 0x86; ipv6[13] = 0xdd; ipv6[14] = 0x60;
        BinaryPrimitives.WriteUInt16BigEndian(ipv6.AsSpan(18), 22); ipv6[20] = 6; ipv6[29] = 1; ipv6[45] = 2;
        ipv6[54] = 1; ipv6[56] = 2; ipv6[66] = 0x50; ipv6[74] = (byte)'v'; ipv6[75] = (byte)'6';
        assembler.ProcessFrame(ipv6, now);
        Require(string.Concat(chunks) == "v6", "IPv6 TCP parsing");
        // Lengths, VLAN tags, extension headers, and truncated frames must be safely ignored.
        for (int length = 0; length < 54; length++) assembler.ProcessFrame(Frame(0, "test").AsSpan(0, length), now);
    }
    private static byte[] Frame(uint sequence, string value, byte flags = 0x10)
    {
        byte[] payload = Encoding.ASCII.GetBytes(value); byte[] frame = new byte[54 + payload.Length];
        frame[12] = 8; frame[14] = 0x45; frame[23] = 6;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(16), (ushort)(40 + payload.Length));
        frame[26] = 10; frame[29] = 1; frame[30] = 10; frame[33] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(34), 1234); BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(36), 4321);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(38), sequence); frame[46] = 0x50; frame[47] = flags;
        payload.CopyTo(frame, 54); return frame;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Capture self-test failed: " + message); }
}
