using System.Text;

namespace server;

public class IrcCommand
{
    public string Prefix { get; set; }
    public string Command { get; set; }
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public string Trailing { get; set; }
}

public static class Parser
{
    public static IrcCommand ParseIrcLine(string line)
    {
        IrcCommand cmd = new IrcCommand();
        
        if (line.StartsWith(":"))
        {
            int spaceIdx = line.IndexOf(' ');
            if (spaceIdx > 0)
            {
                cmd.Prefix = line.Substring(1, spaceIdx - 1);
                line = line.Substring(spaceIdx + 1);
            }
        }
        
        string trailing = null;
        int trailingIdx = line.IndexOf(" :");
        if (trailingIdx >= 0)
        {
            trailing = line.Substring(trailingIdx + 2);
            line = line.Substring(0, trailingIdx);
        }
        cmd.Trailing = trailing;
        
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            cmd.Command = parts[0].ToUpper();
            if (parts.Length > 1)
                cmd.Parameters = parts.Skip(1).ToArray();
        }

        return cmd;
    }
}

public static class Handler
{
    public static async Task SendCommand(this IrcUser user, string command, string prefix = "", string[] parameters = null, string trailing = "")
    {
        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrEmpty(prefix))
            sb.Append(':').Append(prefix).Append(' ');

        sb.Append(command);

        if (parameters is { Length: > 0 })
            sb.Append(' ').Append(string.Join(" ", parameters));

        if (!string.IsNullOrEmpty(trailing))
        {
            if (trailing.Contains(' '))
                sb.Append(" :").Append(trailing);
            else
                sb.Append(' ').Append(trailing);
        }

        Logger.FromServer(user.Nick, sb.ToString());
        await user.Writer.WriteLineAsync(sb.ToString());
    }
    
    public static async Task SendCommand(this StreamWriter writer, string command, string prefix = "", string[] parameters = null, string trailing = "")
    {
        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrEmpty(prefix))
            sb.Append(':').Append(prefix).Append(' ');

        sb.Append(command);

        if (parameters is { Length: > 0 })
            sb.Append(' ').Append(string.Join(" ", parameters));

        if (!string.IsNullOrEmpty(trailing))
        {
            if (trailing.Contains(' '))
                sb.Append(" :").Append(trailing);
            else
                sb.Append(' ').Append(trailing);
        }

        Logger.FromServer("???", sb.ToString());
        await writer.WriteLineAsync(sb.ToString());
    }

    public static async Task ReadCommand(string nickname, string line)
    {
        Program.ConnectedClients.TryGetValue(nickname, out var user);
        if (user == null)
            throw new Exception($"Unable to get user {nickname}");

        IrcCommand command = Parser.ParseIrcLine(line);

        if (command.Command == "PING")
        {
            await user.SendCommand("PONG", parameters: [command.Parameters[0]]);
        }

        if (command.Command == "PONG")
        {
            if (command.Trailing == ServerConfiguration.ServerName || command.Parameters[0] == ServerConfiguration.ServerName)
            {
                user.LastActivity = DateTime.UtcNow;
                user.WaitingForPong = false;
            }
        }

        if (command.Command == "MODE")
        {
            if (command.Parameters[0].StartsWith('#'))
            {
                await user.SendCommand("324", ServerConfiguration.ServerName, [nickname, command.Parameters[0]]);
            }
            else
            {
                Program.ConnectedClients.TryGetValue(command.Parameters[0], out var value);
                bool adding = command.Parameters[1][0] == '+';
                if (adding)
                {
                    for (int i = 1; i < command.Parameters[1].Length; i++)
                    {
                        switch (command.Parameters[1][i])
                        {
                            case 'i':
                                value.Modes |= UserMode.Invisible;
                                break;
                        }
                    }
                }
                Logger.Debug(command.Parameters[0], $"setting mode {command.Parameters[1]}");
            }
        }

        if (command.Command == "JOIN")
        {
            IrcChannel channel;
            if (!Program.ChannelList.ContainsKey(command.Parameters[0]))
            {
                // make a new one
                channel = new IrcChannel() { Name = command.Parameters[0] };
                lock (channel.UsersLock)
                {
                    channel.Users.Add(nickname);
                }
                
                channel.UserModes[nickname] = ChannelUserMode.Operator;
                await user.SendCommand("MODE", $"{user.Nick}!{user.User}@{user.Host}", [channel.Name, "+o", nickname]);
                Program.ChannelList.TryAdd(command.Parameters[0], channel);
                Logger.Debug(nickname, $"created and joined channel {channel.Name}");
            }
            else
            {
                // add a user to existing channel
                Program.ChannelList.TryGetValue(command.Parameters[0], out channel);

                if (channel == null)
                    throw new Exception("Channel not found but supposedly existing");
                
                lock (channel.UsersLock)
                {
                    channel.Users.Add(nickname);
                }
                
                // read modes for this channel and reapply them
                if (channel.UserModes.TryGetValue(nickname, out var mode))
                {
                    if ((mode & ChannelUserMode.Operator) != 0)
                    {
                        foreach (string channelUser in channel.Users)
                        {
                            Program.ConnectedClients.TryGetValue(channelUser, out IrcUser ircUser);
                            await ircUser.SendCommand("MODE", $"{user.Nick}!{user.User}@{user.Host}", [channel.Name, "+o", nickname ]);
                        }
                    }
                }
                else
                {
                    channel.UserModes.Add(nickname, ChannelUserMode.None);
                }
                
                Logger.Debug(nickname, $"joined channel {channel.Name}");
            }

            List<string> users = new List<string>();
            foreach (string channelUser in channel.Users)
            {
                Program.ConnectedClients.TryGetValue(channelUser, out var ircUser);
                await ircUser.SendCommand("JOIN", $"{user.Nick}!{user.User}@{user.Host}", parameters: [channel.Name]);
                
                //build perms list along the way
                ChannelUserMode mode = channel.UserModes[channelUser];
                if ((mode & ChannelUserMode.Operator) != 0)
                {
                    users.Add("@" + channelUser);
                }
                else
                {
                    users.Add(channelUser);
                }
            }
            
            await user.SendCommand("353", ServerConfiguration.ServerName, [user.Nick, "=", channel.Name], string.Join(' ', users));
            await user.SendCommand("366", ServerConfiguration.ServerName, [user.Nick, channel.Name], "End of users list");
        }

        if (command.Command == "PRIVMSG")
        {
            if (command.Parameters[0].StartsWith('#')) // target is a channel
            {
                Program.ChannelList.TryGetValue(command.Parameters[0], out var channel);
                foreach (var nick in channel.Users)
                {
                    if (nick == nickname) continue;
                    if (Program.ConnectedClients.TryGetValue(nick, out var targetUser))
                    {
                        await targetUser.SendCommand(
                            "PRIVMSG",
                            $"{user.Nick}!{user.User}@{user.Host}",
                            parameters: [channel.Name],
                            trailing: command.Trailing
                        );
                    }
                }      
            }
        }
    }
}