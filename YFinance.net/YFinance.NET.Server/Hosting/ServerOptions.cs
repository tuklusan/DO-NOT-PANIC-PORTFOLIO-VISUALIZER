namespace YFinance.NET.Server.Hosting;

public sealed record ServerOptions(
    int Port,
    bool OwnedMode,
    int? OwnerProcessId,
    int MaxConcurrentClients)
{
    public static ServerOptions Parse(string[] args)
    {
        int port = Protocol.Constants.ProtocolConstants.DefaultPort;
        bool ownedMode = false;
        int? ownerPid = null;
        int maxClients = Protocol.Constants.ProtocolConstants.MaxConcurrentClients;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort):
                    port = parsedPort;
                    i++;
                    break;
                case "--owned":
                    ownedMode = true;
                    break;
                case "--owner-pid" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPid):
                    ownerPid = parsedPid;
                    i++;
                    break;
                case "--max-clients" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedClients):
                    maxClients = parsedClients;
                    i++;
                    break;
            }
        }

        return new ServerOptions(port, ownedMode, ownerPid, maxClients);
    }
}
