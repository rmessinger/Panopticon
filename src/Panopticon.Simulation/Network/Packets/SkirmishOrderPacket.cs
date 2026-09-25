using System.Collections.Generic;
using Panopticon.Logic.Orders;

namespace Panopticon.Network.Packets;

public class SkirmishOrderPacket
{
    public uint Frame { get; set; }
    public List<Order> Orders { get; set; }
}
