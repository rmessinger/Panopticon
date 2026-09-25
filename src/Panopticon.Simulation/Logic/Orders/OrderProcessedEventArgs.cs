using System;
using Panopticon.Logic;

namespace Panopticon.Logic.Orders;

public sealed class OrderProcessedEventArgs : EventArgs
{
    public Order Order { get; }
    public Player Player { get; }

    internal OrderProcessedEventArgs(Order order, Player player)
    {
        Order = order;
        Player = player;
    }
}
