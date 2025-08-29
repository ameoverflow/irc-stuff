namespace server;

public class IrcChannel
{
    public string Name { get; set; }
    public string Topic { get; set; } = "";
    public HashSet<string> Users { get; set; } = new HashSet<string>();
    public Dictionary<string, ChannelUserMode> UserModes { get; set; } = new Dictionary<string, ChannelUserMode>();
    public ChannelMode Modes { get; set; } = ChannelMode.None;
    public object UsersLock = new object();
}