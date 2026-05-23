using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Data.Rep;
using OpenSage.Logic.Orders;

namespace OpenSage.Network;

public sealed class NetworkMessageBuffer : DisposableBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IConnection _connection;
    private readonly OrderProcessor _orderProcessor;

    private List<Order> _localOrders;
    private uint _netFrameNumber;

    public Dictionary<uint, List<Order>> FrameOrders { get; }

    /// <summary>The last frame number processed by <see cref="Tick"/>.</summary>
    public uint CurrentFrame => _netFrameNumber;

    public NetworkMessageBuffer(IGame game, IConnection connection)
    {
        FrameOrders = new Dictionary<uint, List<Order>>();
        _localOrders = new List<Order>();
        _connection = connection;
        _orderProcessor = new OrderProcessor(game);
    }

    public void AddLocalOrder(Order order)
    {
        _localOrders.Add(order);
    }

    /// <summary>
    /// Immediately processes <paramref name="orders"/> through the <see cref="OrderProcessor"/>,
    /// bypassing the frame queue. Intended for test-harness runtime injection.
    /// </summary>
    internal void InjectOrdersNow(IEnumerable<Order> orders) =>
        _orderProcessor.Process(orders);

    internal void Tick()
    {
        _connection.Send(_netFrameNumber, _localOrders);

        // create a new list instead of clearing, otherwise
        // we would need to copy the list in _connection.Send
        _localOrders = new List<Order>();

        _connection.Receive(
            _netFrameNumber,
            (frame, order) =>
            {
                if (frame < _netFrameNumber)
                {
                    throw new InvalidOperationException("This should not be possible, Receive should block until all orders are available.");
                }

                Logger.Trace($"Storing order {order.OrderType} for frame {frame}");

                if (!FrameOrders.TryGetValue(frame, out var orders))
                {
                    FrameOrders.Add(frame, orders = new List<Order>());
                }
                orders.Add(order);
            });

        if (FrameOrders.TryGetValue(_netFrameNumber, out var frameOrders))
        {
            _orderProcessor.Process(frameOrders);
        }

        _netFrameNumber++;
    }

    /// <summary>
    /// Saves all recorded orders to a <c>.rep</c> file at <paramref name="outputPath"/>.
    /// The directory is created if it does not exist.
    /// </summary>
    public void SaveReplay(string outputPath, ReplayHeader header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Flatten FrameOrders into an ordered sequence of (frame, order) pairs
        var orderedFrames = FrameOrders
            .OrderBy(kvp => kvp.Key)
            .SelectMany(kvp => kvp.Value.Select(o => (frame: kvp.Key, order: o)));

        using var stream = File.Create(outputPath);
        ReplayFile.Write(stream, header, orderedFrames);

        Logger.Info($"Replay saved to {outputPath}");
    }
}
