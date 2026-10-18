using System.Buffers.Binary;

namespace LocalDps.Protocol;

/// <summary>
/// Width-aware record lane for identity/social/party messages. Feed only the
/// server-to-client direction of the selected game connection. Combat uses its
/// separate legacy framing lane. No name scanning or membership inference occurs here.
/// </summary>
public sealed class IdentityRecordStream
{
    private const int MaxRecord = 65546, MaxBuffer = 524288, MaxBundle = 1000000;
    private readonly Dictionary<string, List<byte>> streams = new();
    public event GameRecordHandler? RecordObserved;
    public long RecordsFramed { get; private set; }
    public long BundlesDecoded { get; private set; }
    public long RejectedRecords { get; private set; }
    public void Reset() { streams.Clear(); RecordsFramed = BundlesDecoded = RejectedRecords = 0; }

    public void Feed(string streamId, ReadOnlySpan<byte> payload, long timestampMs)
    {
        if (!streams.TryGetValue(streamId, out var buffer))
        {
            if (streams.Count >= 128) streams.Remove(streams.Keys.First());
            streams[streamId] = buffer = new();
        }
        if (payload.Length > MaxBuffer) { buffer.Clear(); RejectedRecords++; return; }
        if (buffer.Count + payload.Length > MaxBuffer) { buffer.Clear(); RejectedRecords++; }
        buffer.AddRange(payload.ToArray());
        var bytes = buffer.ToArray(); int position = 0, budget = 8192;
        while (position < bytes.Length && budget > 0)
        {
            int prefix = position;
            if (!ReadVar(bytes, ref prefix, out int declared))
            {
                if (bytes.Length - position >= 5) { position++; RejectedRecords++; continue; }
                break;
            }
            if (declared < 6 || declared > MaxRecord) { position++; RejectedRecords++; continue; }
            if (prefix + 2 > bytes.Length) break;
            // Server records are length-delimited even for unknown families.
            // Do not discard a newly encountered family and accidentally scan its
            // payload as records. Observers validate their own opcode and layout.
            int length = declared - 4 + prefix - position;
            if (position + length > bytes.Length) break;
            Walk(bytes.AsSpan(position, length), timestampMs, 0, ref budget);
            position += length;
        }
        if (position > 0) buffer.RemoveRange(0, position);
        if (budget <= 0) { buffer.Clear(); RejectedRecords++; }
    }

    private void Walk(ReadOnlySpan<byte> record, long time, int depth, ref int budget)
    {
        if (depth > 4 || budget-- <= 0) { RejectedRecords++; return; }
        int p = 0;
        if (!ReadVar(record, ref p, out int declared) || p + 2 > record.Length || declared - 4 + p != record.Length) { RejectedRecords++; return; }
        RecordsFramed++; RecordObserved?.Invoke(record, time);
        bool bundle = record[p] == 255 && record[p + 1] == 255; p += 2;
        if (!bundle) return;
        if (p + 4 > record.Length) { RejectedRecords++; return; }
        int expected = BinaryPrimitives.ReadInt32LittleEndian(record[p..]); p += 4;
        if (expected < 1 || expected > MaxBundle) { RejectedRecords++; return; }
        byte[]? plain = Unpack(record[p..], expected);
        if (plain == null) { RejectedRecords++; return; }
        BundlesDecoded++;
        int start = 0;
        while (start < plain.Length && budget > 0)
        {
            if (plain[start] == 0) { start++; continue; }
            int cursor = start;
            if (!ReadVar(plain, ref cursor, out int size) || size < 6 || size > MaxRecord) { RejectedRecords++; return; }
            int length = size - 4 + cursor - start;
            if (start + length > plain.Length || length < cursor - start + 2) { RejectedRecords++; return; }
            Walk(plain.AsSpan(start, length), time, depth + 1, ref budget); start += length;
        }
    }
    private static bool ReadVar(ReadOnlySpan<byte> bytes, ref int p, out int value)
    {
        value = 0;
        for (int i = 0; i < 5 && p < bytes.Length; i++)
        {
            byte b = bytes[p++]; if (i == 4 && (b & 0xf8) != 0) return false;
            value |= (b & 127) << (7 * i); if ((b & 128) == 0) return true;
        }
        return false;
    }
    private static byte[]? Unpack(ReadOnlySpan<byte> input, int expected)
    {
        byte[] output = new byte[expected]; int i = 0, o = 0;
        while (i < input.Length)
        {
            int token = input[i++], literal = token >> 4;
            if (literal == 15) { int next; do { if (i >= input.Length) return null; next = input[i++]; literal += next; } while (next == 255); }
            if (i + literal > input.Length || o + literal > expected) return null;
            input.Slice(i, literal).CopyTo(output.AsSpan(o)); i += literal; o += literal;
            if (i == input.Length) return o == expected ? output : null;
            if (i + 2 > input.Length) return null;
            int distance = input[i] | input[i + 1] << 8; i += 2;
            if (distance == 0 || distance > o) return null;
            int match = token & 15;
            if (match == 15) { int next; do { if (i >= input.Length) return null; next = input[i++]; match += next; } while (next == 255); }
            match += 4; if (o + match > expected) return null;
            for (int k = 0; k < match; k++) { output[o] = output[o - distance]; o++; }
        }
        return o == expected ? output : null;
    }
}
