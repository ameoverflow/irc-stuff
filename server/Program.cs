using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace server;

static class Program
{
    public static ConcurrentDictionary<string, IrcUser> ConnectedClients = new ConcurrentDictionary<string, IrcUser>();

    public static ConcurrentDictionary<string, IrcChannel>
        ChannelList = new ConcurrentDictionary<string, IrcChannel>();
    
    static async Task Main(string[] args)
    {
        var listener = new TcpListener(IPAddress.Any, 6667);
        listener.Start();
        Console.WriteLine("irc server listening on port 6667...");

        _ = Task.Run(HeartbeatLoop);

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = HandleClient(client).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    Console.WriteLine($"client error: {t.Exception}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static async Task? HandleClient(TcpClient client)
    {
        var stream = client.GetStream();
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };

        string nickname = "anon";
        string username = "anon";
        string host = "127.0.0.1";
        
        while (true)
        {
            string line = await reader.ReadLineAsync();
            if (line == null) break;
            
            Logger.ToServer("???", line);

            if (line.StartsWith("NICK "))
            {
                nickname = line.Substring(5).Trim();
                Logger.Debug("???", $"client nickname is {nickname}");
            }
            else if (line.StartsWith("USER "))
            {
                username = line.Split(' ')[1];
                host = line.Split(' ')[3];
                
                Logger.Debug("???", $"client username is {username}");
                break;
            }
            else if (line.StartsWith("CAP LS"))
                await writer.SendCommand("CAP", prefix: ServerConfiguration.ServerName, parameters: ["*", "LS"]);
        }
        
        // done with handshake, welcome the user, and register in stuff

        ConnectedClients.TryAdd(nickname,
            new IrcUser() { Nick = nickname, Client = client, Writer = writer, User = username, Host = host});

        IrcUser user = new IrcUser() { Nick = nickname, Client = client, Writer = writer, User = username, Host = host };
        
        await user.SendCommand("001", ServerConfiguration.ServerName, [nickname], "Welcome to the server");
        await user.SendCommand("002", ServerConfiguration.ServerName, [nickname], "Host is running stupidIRC 0.0.1");
        await user.SendCommand("004", ServerConfiguration.ServerName, [nickname], "stupidIRC-0.0.1 o o");
        await user.SendCommand("375", ServerConfiguration.ServerName, [nickname], "- Message of the day:");
        await user.SendCommand("372", ServerConfiguration.ServerName, [nickname], "- dolar od strony przepompowni");
        await user.SendCommand("376", ServerConfiguration.ServerName, [nickname], "End of MOTD");
        
        Logger.Debug(nickname, "handshake complete");
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            
            // special case quit command handle here
            
            if (line.StartsWith("QUIT"))
            {
                break;
            }
            Logger.ToServer(nickname, line);
            await Handler.ReadCommand(nickname, line);
        }

        client.Close();
        ConnectedClients.TryRemove(nickname, out _);
        Console.WriteLine("connection closed");
    }

    private static async Task? HeartbeatLoop()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync())
        {
            foreach (var kvpUser in ConnectedClients)
            {
                IrcUser user = kvpUser.Value;
                DateTime now = DateTime.UtcNow;
                
                if (!user.WaitingForPong && now - user.LastActivity > TimeSpan.FromMinutes(5))
                {
                    Logger.Debug(user.Nick, "seems to be dead, pinging");
                    await user.SendCommand("PING", parameters: [ServerConfiguration.ServerName]);
                    user.LastPing = now;
                    user.WaitingForPong = true;
                }
                
                if (user.WaitingForPong && now - user.LastPing > TimeSpan.FromSeconds(30))
                {
                    await Disconnect(user, "Ping timeout");
                }
            }
        }
    }

    private static async Task Disconnect(IrcUser user, string reason = "Disconnected")
    {
        Logger.Debug(user.Nick, "disconnecting");
        await user.SendCommand("ERROR", trailing: reason);
        user.Client.Close();
        ConnectedClients.TryRemove(user.Nick, out _);
    }
}