using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace LocalDps.Capture;

public sealed record CaptureDevice(string Name, string Description);

/// <summary>Explicit-start, passive Npcap reader with bounded buffering and offline classic-PCAP replay.</summary>
public sealed class PacketCaptureService : IDisposable
{
    private readonly TcpStreamAssembler assembler = new();
    private CancellationTokenSource? cancellation;
    private Task? reader, worker;
    private IntPtr handle;
    private long dropped;
    private long packets;
    public event Action<byte[], DateTimeOffset, string>? StreamChunk;
    public event Action<string>? Status;
    public bool IsRunning => cancellation != null && reader is {IsCompleted:false} && worker is {IsCompleted:false};
    public long DroppedFrames => Interlocked.Read(ref dropped);
    public long PacketsReceived => Interlocked.Read(ref packets);
    public PacketCaptureService()
    {
        assembler.StreamChunk += (bytes, time, flow) => StreamChunk?.Invoke(bytes, time, flow);
        assembler.Status += message => Status?.Invoke(message);
    }
    public static IReadOnlyList<CaptureDevice> ListDevices()
    {
        Native.EnsureLoaded();
        byte[] error = new byte[256];
        if (Native.pcap_findalldevs(out var head, error) != 0) throw new InvalidOperationException(Error(error));
        var devices = new List<CaptureDevice>();
        try
        {
            for (var node = head; node != IntPtr.Zero;)
            {
                var item = Marshal.PtrToStructure<Native.Device>(node);
                string name = Marshal.PtrToStringAnsi(item.Name) ?? "";
                if (name.Length > 0) devices.Add(new(name, Marshal.PtrToStringAnsi(item.Description) ?? name));
                node = item.Next;
            }
        }
        finally { Native.pcap_freealldevs(head); }
        return devices;
    }
    public void Start(string deviceName, int? tcpPort = null)
    {
        if (tcpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(tcpPort));
        StartFiltered(deviceName,tcpPort.HasValue ? $"tcp port {tcpPort.Value}" : "tcp");
    }
    public void StartGamePorts(string deviceName,IReadOnlyCollection<int> ports)
    {
        if(ports.Count==0||ports.Count>128||ports.Any(p=>p<1||p>65535))throw new ArgumentException("Invalid game connection ports.");
        StartFiltered(deviceName,"tcp and ("+string.Join(" or ",ports.Distinct().Select(p=>$"port {p}"))+")");
    }
    void StartFiltered(string deviceName,string filter)
    {
        if (IsRunning) throw new InvalidOperationException("Capture is already running.");
        if(cancellation!=null)Stop();
        Native.EnsureLoaded();
        byte[] error = new byte[256];
        // Non-promiscuous mode. 100 ms timeout permits prompt stopping.
        handle = Native.pcap_open_live(deviceName, 65535, 0, 100, error);
        if (handle == IntPtr.Zero) throw new InvalidOperationException(Error(error));
        try
        {
            int linkType = Native.pcap_datalink(handle);
            if (linkType is not (0 or 1 or 101)) throw new NotSupportedException($"Unsupported capture link type {linkType}.");
            if (Native.pcap_setnonblock(handle, 1, error) != 0) throw new InvalidOperationException(Error(error));
            if (Native.pcap_compile(handle, out var program, filter, 1, uint.MaxValue) != 0) throw new InvalidOperationException(Native.GetError(handle));
            try { if (Native.pcap_setfilter(handle, ref program) != 0) throw new InvalidOperationException(Native.GetError(handle)); }
            finally { Native.pcap_freecode(ref program); }
            assembler.Reset(); Interlocked.Exchange(ref dropped, 0); Interlocked.Exchange(ref packets,0);
            var channel = Channel.CreateBounded<(byte[] Data, DateTimeOffset Time)>(new BoundedChannelOptions(2048) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token; var activeHandle = handle;
            worker = Task.Run(async () =>
            {
                try
                {
                    await foreach (var frame in channel.Reader.ReadAllAsync())
                        assembler.ProcessFrame(frame.Data, frame.Time, linkType);
                }
                catch(Exception ex){Status?.Invoke($"Capture stopped: processing failed: {ex.Message}");cancellation?.Cancel();}
            });
            reader = Task.Run(() =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int result = Native.pcap_next_ex(activeHandle, out var headerPointer, out var dataPointer);
                        if (result == 0) { Thread.Sleep(10); continue; }
                        if (result == -2) break;
                        if (result < 0) throw new InvalidOperationException(Native.GetError(activeHandle));
                        Interlocked.Increment(ref packets);
                        var header = Marshal.PtrToStructure<Native.PacketHeader>(headerPointer);
                        if (header.CapturedLength > 65535) continue;
                        byte[] bytes = new byte[header.CapturedLength]; Marshal.Copy(dataPointer, bytes, 0, bytes.Length);
                        var time = DateTimeOffset.FromUnixTimeSeconds(header.Seconds).AddTicks(header.Microseconds * 10L);
                        if (!channel.Writer.TryWrite((bytes, time))) Interlocked.Increment(ref dropped);
                    }
                }
                catch (Exception ex) { Status?.Invoke($"Capture stopped: {ex.Message}"); }
                finally { channel.Writer.TryComplete(); }
            });
            Status?.Invoke($"Passive capture started ({filter}).");
        }
        catch { Native.pcap_close(handle); handle = IntPtr.Zero; throw; }
    }
    public void Stop()
    {
        var source = cancellation;
        if (source == null) return;
        source.Cancel();
        // Nonblocking pcap_next_ex permits stopping even on a completely idle adapter.
        try { reader?.GetAwaiter().GetResult(); worker?.GetAwaiter().GetResult(); }
        finally
        {
            Native.pcap_close(handle); handle = IntPtr.Zero;
            source.Dispose(); cancellation = null; reader = worker = null;
        }
        Status?.Invoke($"Capture stopped. Dropped frames: {DroppedFrames}.");
    }
    public void ReplayPcap(string path)
    {
        if (IsRunning) throw new InvalidOperationException("Stop live capture before replaying a file.");
        using var input = File.OpenRead(path);
        Span<byte> global = stackalloc byte[24]; input.ReadExactly(global);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(global);
        bool little = magic is 0xa1b2c3d4 or 0xa1b23c4d;
        bool nano = magic is 0xa1b23c4d or 0x4d3cb2a1;
        if (!little && magic is not (0xd4c3b2a1 or 0x4d3cb2a1)) throw new InvalidDataException("Expected a classic PCAP file (PCAPNG is not supported).");
        uint Read32(ReadOnlySpan<byte> value) => little ? BinaryPrimitives.ReadUInt32LittleEndian(value) : BinaryPrimitives.ReadUInt32BigEndian(value);
        int linkType = checked((int)(Read32(global[20..]) & 0xffff));
        if (linkType is not (0 or 1 or 101)) throw new NotSupportedException($"Unsupported PCAP link type {linkType}.");
        assembler.Reset(); Interlocked.Exchange(ref packets,0); Interlocked.Exchange(ref dropped,0); Span<byte> header = stackalloc byte[16]; long count = 0;
        while (input.Position < input.Length)
        {
            input.ReadExactly(header); uint length = Read32(header[8..]);
            if (length > 1024 * 1024 || length > input.Length - input.Position) throw new InvalidDataException("Invalid PCAP packet length.");
            byte[] data = new byte[length]; input.ReadExactly(data);
            var time = DateTimeOffset.FromUnixTimeSeconds(Read32(header)).AddTicks(nano ? Read32(header[4..]) / 100L : Read32(header[4..]) * 10L);
            assembler.ProcessFrame(data, time, linkType); count++; Interlocked.Increment(ref packets);
        }
        Status?.Invoke($"Replayed {count:N0} packets locally.");
    }
    public void Dispose() => Stop();
    private static string Error(byte[] error) => System.Text.Encoding.UTF8.GetString(error).TrimEnd('\0');

    private static class Native
    {
        private const string Library = "wpcap.dll";
        private static readonly object Gate = new();
        private static bool loaded;
        internal static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (loaded) return;
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live capture requires Windows and Npcap.");
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Npcap");
                string library = Path.Combine(directory, Library);
                if (!File.Exists(library)) throw new FileNotFoundException("Npcap was not found. Install Npcap to enable passive capture.", library);
                // Resolve only the system Npcap directory; never search the current working directory.
                if (LoadLibraryEx(library, IntPtr.Zero, 0x00000100 | 0x00001000) == IntPtr.Zero) throw new InvalidOperationException("Could not load the system Npcap library.");
                loaded = true;
            }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Device { public IntPtr Next, Name, Description, Addresses; public uint Flags; }
        // Windows timeval uses 32-bit long, including on x64.
        [StructLayout(LayoutKind.Sequential)] internal struct PacketHeader { public int Seconds, Microseconds; public uint CapturedLength, OriginalLength; }
        [StructLayout(LayoutKind.Sequential)] internal struct Program { public uint Length; public IntPtr Instructions; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_findalldevs(out IntPtr devices, byte[] error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_freealldevs(IntPtr devices);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern IntPtr pcap_open_live(string device, int snaplen, int promiscuous, int timeout, byte[] error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_datalink(IntPtr capture);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_setnonblock(IntPtr capture, int nonblocking, byte[] error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int pcap_compile(IntPtr capture, out Program program, string filter, int optimize, uint netmask);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_setfilter(IntPtr capture, ref Program program);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_freecode(ref Program program);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_next_ex(IntPtr capture, out IntPtr header, out IntPtr data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_close(IntPtr capture);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr pcap_geterr(IntPtr capture);
        internal static string GetError(IntPtr capture) => Marshal.PtrToStringAnsi(pcap_geterr(capture)) ?? "Npcap error";
    }
}
