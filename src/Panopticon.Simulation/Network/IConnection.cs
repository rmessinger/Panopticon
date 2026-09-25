using System;
using System.Collections.Generic;
using Panopticon.Logic.Orders;

namespace Panopticon.Network;

public interface IConnection : IDisposable
{
    void Send(uint frame, List<Order> orders);
    void Receive(uint frame, Action<uint, Order> packetFn);
}
