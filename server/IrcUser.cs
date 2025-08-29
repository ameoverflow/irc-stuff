using System.Net.Sockets;

namespace server;

public class IrcUser
{
    public string Nick;
    public string User;
    public string Host;
    public TcpClient Client;
    public StreamWriter Writer;
    public HashSet<string> Channels = new HashSet<string>();
    public UserMode Modes = UserMode.None;
    
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime LastPing { get; set; } = DateTime.UtcNow;
    public bool WaitingForPong { get; set; }
}
