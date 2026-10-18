using System.Text.Json;
namespace LocalDps.Capture;
public static class RecordedStreamReplay
{
    public static void Read(string path,Action<byte[],DateTimeOffset,string> consume)
    {
        if(new FileInfo(path).Length>256*1024*1024)throw new InvalidDataException("Recording exceeds 256 MiB.");
        foreach(string line in File.ReadLines(path))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            if(line.Length>2*1024*1024)throw new InvalidDataException("Recording chunk exceeds limit.");
            using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
            string flow=root.GetProperty("flow").GetString()??throw new InvalidDataException("Missing flow.");
            if(flow.Length>512)throw new InvalidDataException("Invalid flow identifier.");
            byte[] bytes=Convert.FromBase64String(root.GetProperty("data").GetString()??"");
            if(bytes.Length>1024*1024)throw new InvalidDataException("Chunk exceeds limit.");
            consume(bytes,DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("time").GetInt64()),flow);
        }
    }
}
