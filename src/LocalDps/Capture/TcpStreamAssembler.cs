using System.Buffers.Binary;
using System.Net;

namespace LocalDps.Capture;

/// <summary>Bounded, directional TCP reconstruction. It never sends network traffic.</summary>
public sealed class TcpStreamAssembler
{
    private sealed class StreamState
    {
        public uint Next;
        public DateTimeOffset Last;
        public Dictionary<uint, byte[]> Pending = new();
        public int Buffered;
    }
    private readonly Dictionary<string, StreamState> streams = new();
    private const int MaxFlows = 1024, MaxBuffered = 1024 * 1024, MaxSegments = 256;
    public event Action<byte[], DateTimeOffset, string>? StreamChunk;
    public event Action<string>? Status;
    public void Reset() => streams.Clear();

    public void ProcessFrame(ReadOnlySpan<byte> frame, DateTimeOffset timestamp, int linkType = 1)
    {
        int offset; ushort protocol;
        if (linkType == 1)
        {
            if (frame.Length < 14) return;
            offset = 14; protocol = BinaryPrimitives.ReadUInt16BigEndian(frame[12..]);
            for (var tag = 0; tag < 2 && (protocol == 0x8100 || protocol == 0x88a8); tag++)
            {
                if (frame.Length < offset + 4) return;
                protocol = BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 2)..]); offset += 4;
            }
        }
        else if (linkType == 101) { offset = 0; protocol = frame.Length > 0 && frame[0] >> 4 == 6 ? (ushort)0x86dd : (ushort)0x0800; }
        else if (linkType == 0 && frame.Length >= 5) { offset = 4; protocol = frame[offset] >> 4 == 6 ? (ushort)0x86dd : (ushort)0x0800; }
        else return;
        string source, destination; int end;
        if (protocol == 0x0800)
        {
            if (frame.Length < offset + 20 || frame[offset] >> 4 != 4) return;
            int header = (frame[offset] & 15) * 4, length = BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 2)..]);
            if (header < 20 || length < header || frame.Length < offset + length || frame[offset + 9] != 6) return;
            // Fragmented IP requires a separate reassembler; skip instead of corrupting TCP.
            if ((BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 6)..]) & 0x3fff) != 0) return;
            source = new IPAddress(frame.Slice(offset + 12, 4)).ToString(); destination = new IPAddress(frame.Slice(offset + 16, 4)).ToString();
            end = offset + length; offset += header;
        }
        else if (protocol == 0x86dd)
        {
            if (frame.Length < offset + 40 || frame[offset] >> 4 != 6) return;
            end = offset + 40 + BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 4)..]);
            if (end > frame.Length) return;
            source = new IPAddress(frame.Slice(offset + 8, 16)).ToString(); destination = new IPAddress(frame.Slice(offset + 24, 16)).ToString();
            int next = frame[offset + 6]; offset += 40;
            for (int n = 0; next != 6 && n < 8; n++)
            {
                if (offset + 2 > end) return;
                int size;
                if (next is 0 or 43 or 60) size = (frame[offset + 1] + 1) * 8;
                else if (next == 51) size = (frame[offset + 1] + 2) * 4;
                else return; // Includes fragments and encrypted ESP.
                next = frame[offset]; offset += size;
                if (offset > end) return;
            }
            if (next != 6) return;
        }
        else return;
        if (offset + 20 > end) return;
        int tcpHeader = (frame[offset + 12] >> 4) * 4;
        if (tcpHeader < 20 || offset + tcpHeader > end) return;
        int sport = BinaryPrimitives.ReadUInt16BigEndian(frame[offset..]), dport = BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 2)..]);
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(frame[(offset + 4)..]); byte flags = frame[offset + 13];
        string key = $"[{source}]:{sport}>[{destination}]:{dport}";
        bool syn = (flags & 2) != 0;
        if (syn) { streams.Remove(key); sequence++; }
        var payload = frame.Slice(offset + tcpHeader, end - offset - tcpHeader);
        if (!streams.TryGetValue(key, out var state))
        {
            if (payload.IsEmpty && !syn) return;
            if (streams.Count >= MaxFlows) streams.Remove(streams.MinBy(x => x.Value.Last).Key);
            streams[key] = state = new StreamState { Next = sequence, Last = timestamp };
        }
        state.Last = timestamp;
        if (!payload.IsEmpty) Accept(state, sequence, payload, timestamp, key);
        if ((flags & 5) != 0) streams.Remove(key);
        // Bound inactive flows even if capture timestamps move backwards.
        foreach (var old in streams.Where(x => timestamp - x.Value.Last > TimeSpan.FromMinutes(2)).Select(x => x.Key).ToArray()) streams.Remove(old);
    }

    private void Accept(StreamState state, uint sequence, ReadOnlySpan<byte> payload, DateTimeOffset time, string key)
    {
        int delta = unchecked((int)(sequence - state.Next));
        if (delta < 0)
        {
            long skip = -(long)delta;
            if (skip >= payload.Length) return;
            payload = payload[(int)skip..]; sequence = state.Next; delta = 0;
        }
        if (delta > 0)
        {
            if (delta > MaxBuffered || state.Buffered + payload.Length > MaxBuffered || state.Pending.Count >= MaxSegments)
            {
                streams.Remove(key); Status?.Invoke($"TCP gap exceeded limits; reset {key}"); return;
            }
            if (!state.Pending.TryGetValue(sequence, out var previous) || previous.Length < payload.Length)
            {
                if (previous != null) state.Buffered -= previous.Length;
                state.Pending[sequence] = payload.ToArray(); state.Buffered += payload.Length;
            }
            return;
        }
        Emit(state, payload.ToArray(), time, key);
        while (state.Pending.Count > 0)
        {
            var ready = state.Pending.FirstOrDefault(x => unchecked((int)(x.Key - state.Next)) <= 0);
            if (ready.Value == null) break;
            state.Pending.Remove(ready.Key); state.Buffered -= ready.Value.Length;
            long skip = -(long)unchecked((int)(ready.Key - state.Next));
            if (skip < ready.Value.Length) Emit(state, ready.Value.AsSpan((int)skip).ToArray(), time, key);
        }
    }
    private void Emit(StreamState state, byte[] data, DateTimeOffset time, string key)
    {
        state.Next = unchecked(state.Next + (uint)data.Length); StreamChunk?.Invoke(data, time, key);
    }
}
