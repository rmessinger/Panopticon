using System;
using System.Net;

namespace Panopticon.Network;

public class LobbyPlayer
{
    public string ClientId { get; set; }
    public string Username { get; set; }
    public bool IsHosting { get; set; }
    public IPEndPoint EndPoint { get; set; }
    public DateTime LastSeen { get; set; }
}
