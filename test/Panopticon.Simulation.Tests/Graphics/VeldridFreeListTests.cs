using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid.Vk;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class VeldridFreeListTests
{
    private const int BlockCount = 64;
    private const ulong BlockSize = 4096;
    private const ulong ChunkSize = BlockCount * BlockSize;

    [Fact]
    public void InsertIntoEmptyListAddsSingleBlock()
    {
        List<VkMemoryBlock> freeBlocks = new List<VkMemoryBlock>();

        Insert(freeBlocks, 100, 50);

        AssertRanges(freeBlocks, (100, 50));
    }

    [Fact]
    public void InsertAtHeadKeepsOrderAndGap()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((100, 50), (300, 50));

        Insert(freeBlocks, 0, 20);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 20), (100, 50), (300, 50));
    }

    [Fact]
    public void InsertInMiddleKeepsOrderAndGaps()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (100, 10), (300, 10));

        Insert(freeBlocks, 200, 10);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 10), (100, 10), (200, 10), (300, 10));
    }

    [Fact]
    public void InsertAtTailKeepsOrderAndGap()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (100, 10));

        Insert(freeBlocks, 500, 10);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 10), (100, 10), (500, 10));
    }

    [Fact]
    public void InsertMergesWithPredecessorOnly()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (50, 10));

        Insert(freeBlocks, 10, 20);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 30), (50, 10));
    }

    [Fact]
    public void InsertMergesWithSuccessorOnly()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (50, 10));

        Insert(freeBlocks, 40, 10);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 10), (40, 20));
    }

    [Fact]
    public void InsertMergesWithBothNeighbors()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (30, 10));

        Insert(freeBlocks, 10, 20);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 40));
    }

    [Fact]
    public void InsertWithoutAdjacentNeighborsAddsOneBlock()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10), (100, 10));

        Insert(freeBlocks, 50, 10);

        AssertSortedWithGaps(freeBlocks);
        AssertRanges(freeBlocks, (0, 10), (50, 10), (100, 10));
    }

    [Fact]
    public void MergedBlockUsesSuppliedMemoryTypeIndex()
    {
        List<VkMemoryBlock> freeBlocks = SeededList((0, 10));

        VkChunkFreeList.Insert(freeBlocks, Block(10, 10), 7, IntPtr.Zero);

        AssertRanges(freeBlocks, (0, 20));
        Assert.Equal(7u, freeBlocks[0].MemoryTypeIndex);
    }

    [Fact]
    public void FullCycleStrideOrderCoalescesToSingleChunk()
    {
        RunFullCycle(Enumerable.Range(0, BlockCount).Select(i => (i * 37) % BlockCount).ToArray());
    }

    [Fact]
    public void FullCycleReverseOrderCoalescesToSingleChunk()
    {
        RunFullCycle(Enumerable.Range(0, BlockCount).Select(i => BlockCount - 1 - i).ToArray());
    }

    [Fact]
    public void FullCycleAscendingOrderCoalescesToSingleChunk()
    {
        RunFullCycle(Enumerable.Range(0, BlockCount).ToArray());
    }

    [Fact]
    public void FullCycleSeededShuffleOrderCoalescesToSingleChunk()
    {
        int[] order = Enumerable.Range(0, BlockCount).ToArray();
        Random random = new Random(12345);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        RunFullCycle(order);
    }

    private static void RunFullCycle(int[] freeOrder)
    {
        Assert.Equal(BlockCount, freeOrder.Distinct().Count());

        VkMemoryBlock[] live = CarveBlocks();
        bool[] freed = new bool[BlockCount];
        List<VkMemoryBlock> freeBlocks = new List<VkMemoryBlock>();

        foreach (int index in freeOrder)
        {
            freed[index] = true;
            VkChunkFreeList.Insert(freeBlocks, live[index], 0, IntPtr.Zero);

            AssertSortedWithGaps(freeBlocks);
            AssertRanges(freeBlocks, ExpectedRuns(freed));

            ulong freeBytes = 0;
            foreach (VkMemoryBlock block in freeBlocks)
            {
                freeBytes += block.Size;
            }

            ulong liveBytes = (ulong)freed.Count(isFreed => !isFreed) * BlockSize;
            Assert.Equal(ChunkSize, freeBytes + liveBytes);
        }

        AssertRanges(freeBlocks, (0, ChunkSize));
    }

    private static VkMemoryBlock[] CarveBlocks()
    {
        VkMemoryBlock[] carved = new VkMemoryBlock[BlockCount];
        VkMemoryBlock remaining = Block(0, ChunkSize);
        for (int i = 0; i < BlockCount; i++)
        {
            carved[i] = Block(remaining.Offset, BlockSize);
            remaining = Block(remaining.Offset + BlockSize, remaining.Size - BlockSize);
        }

        Assert.Equal(0ul, remaining.Size);
        return carved;
    }

    private static (ulong Offset, ulong Size)[] ExpectedRuns(bool[] freed)
    {
        List<(ulong Offset, ulong Size)> runs = new List<(ulong Offset, ulong Size)>();
        int i = 0;
        while (i < freed.Length)
        {
            if (!freed[i])
            {
                i++;
                continue;
            }

            int start = i;
            while (i < freed.Length && freed[i])
            {
                i++;
            }

            runs.Add(((ulong)start * BlockSize, (ulong)(i - start) * BlockSize));
        }

        return runs.ToArray();
    }

    private static VkMemoryBlock Block(ulong offset, ulong size)
    {
        VkMemoryBlock block = default;
        block.Offset = offset;
        block.Size = size;
        return block;
    }

    private static void Insert(List<VkMemoryBlock> freeBlocks, ulong offset, ulong size)
    {
        VkChunkFreeList.Insert(freeBlocks, Block(offset, size), 0, IntPtr.Zero);
    }

    private static List<VkMemoryBlock> SeededList(params (ulong Offset, ulong Size)[] ranges)
    {
        List<VkMemoryBlock> freeBlocks = new List<VkMemoryBlock>();
        foreach ((ulong offset, ulong size) in ranges)
        {
            freeBlocks.Add(Block(offset, size));
        }

        return freeBlocks;
    }

    private static void AssertSortedWithGaps(IReadOnlyList<VkMemoryBlock> blocks)
    {
        for (int i = 1; i < blocks.Count; i++)
        {
            Assert.True(blocks[i - 1].Offset < blocks[i].Offset, "Free blocks are not sorted by offset.");
            Assert.True(blocks[i - 1].End < blocks[i].Offset, "Adjacent free blocks were not coalesced or overlap.");
        }
    }

    private static void AssertRanges(IReadOnlyList<VkMemoryBlock> blocks, params (ulong Offset, ulong Size)[] expected)
    {
        Assert.Equal(expected.Length, blocks.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Offset, blocks[i].Offset);
            Assert.Equal(expected[i].Size, blocks[i].Size);
        }
    }
}
