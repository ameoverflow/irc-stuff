namespace server;

[Flags]
public enum UserMode
{
    None = 0,
    Invisible = 1 << 0,  // +i
    Operator = 1 << 1,  // +o
    WallOps = 1 << 2,  // +w
    ServerNotices = 1 << 3, // +s
}

[Flags]
public enum ChannelUserMode
{
    None = 0,
    Operator = 1 << 0,  // +o
    Voice = 1 << 1   // +v
}

[Flags]
public enum ChannelMode
{
    None = 0,
    InviteOnly = 1 << 0,
    Moderated = 1 << 1, 
    NoExternal = 1 << 2,
    TopicLocked = 1 << 3
}
