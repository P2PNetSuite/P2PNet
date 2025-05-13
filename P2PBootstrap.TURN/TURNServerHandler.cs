using static P2PNet.PeerNetwork;
using System.Diagnostics;
using System.Collections.Concurrent;

public class TurnServerManager
{
    private Process? _process;
    public bool IsRunning => _process != null && !_process.HasExited;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _signalingQueues = new();

    public void StartTURNServer(int port = 3478, string user = "user", string pass = "pass")
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"turn-server.js --port {port} --user {user} --pass {pass}",
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "NodeTurnServer"),
            UseShellExecute = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        _process = Process.Start(psi);
    }

    public void Stop()
    {
        _process?.Kill();
        _process = null;
    }

    public void EnqueueSignalingMessage(string peerId, string message)
    {
        var queue = _signalingQueues.GetOrAdd(peerId, _ => new ConcurrentQueue<string>());
        queue.Enqueue(message);
    }

    public List<string> DequeueSignalingMessages(string peerId)
    {
        var messages = new List<string>();
        if (_signalingQueues.TryGetValue(peerId, out var queue))
        {
            while (queue.TryDequeue(out var msg))
                messages.Add(msg);
        }
        return messages;
    }

}