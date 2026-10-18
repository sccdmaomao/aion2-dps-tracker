using System.Text.Json;
namespace LocalDps.Capture;

/// <summary>Bounded, in-memory game-stream history; written only on explicit save.</summary>
public sealed class DiagnosticRecording
{
    private sealed record Chunk(string flow,long time,string data,int cost);
    private readonly Queue<Chunk> chunks=new();
    private int bytes;
    public int Count=>chunks.Count;
    public void Reset(){chunks.Clear();bytes=0;}
    public void Add(string flow,long time,byte[] data)
    {
        if(data.Length>1024*1024)return;
        string encoded=Convert.ToBase64String(data);int cost=encoded.Length*2+flow.Length*2+64;
        chunks.Enqueue(new(flow,time,encoded,cost));bytes+=cost;
        while(chunks.Count>0&&(bytes>16*1024*1024||chunks.Count>32768||time-chunks.Peek().time>120000))bytes-=chunks.Dequeue().cost;
    }
    public string[] Lines()=>chunks.Select(c=>JsonSerializer.Serialize(new{c.flow,c.time,c.data})).ToArray();
}
