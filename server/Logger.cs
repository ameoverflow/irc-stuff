namespace server;

public static class Logger
{
    public static void ToServer(string nickname, string log)
    {
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] {nickname}: client -> server: {log}".Trim('\r').Trim('\n'));
    }
    
    public static void FromServer(string nickname, string log)
    {
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] {nickname}: client <- server: {log}".Trim('\r').Trim('\n'));
    }

    public static void Debug(string nickname, string log)
    {
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] {nickname}: {log}");
    }
}