using System;
using BenchmarkDotNet.Running;

namespace Panopticon.Benchmarks.DataStructures;

public class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return;
        }

        BenchmarkRunner.Run<BitArray512CheckExisting>();
        BenchmarkRunner.Run<BitArray512SetAllAndCount>();
        BenchmarkRunner.Run<BitArray512SetAllExceptOneAndCount>();
        BenchmarkRunner.Run<BitArray512SetHalfRandomlyAndCount>();
        BenchmarkRunner.Run<QuadtreeInsert>();
        BenchmarkRunner.Run<QuadtreeUpdate>();
        BenchmarkRunner.Run<QuadtreeQuery>();
        BenchmarkRunner.Run<PathfindingQuery>();
        Console.ReadLine();
    }
}
