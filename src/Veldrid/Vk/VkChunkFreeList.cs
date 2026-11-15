using System;
using System.Collections.Generic;

namespace Veldrid.Vk
{
    // Precondition: block does not overlap any entry in freeBlocks (valid frees only).
    internal static unsafe class VkChunkFreeList
    {
        public static void Insert(List<VkMemoryBlock> freeBlocks, VkMemoryBlock block, uint memoryTypeIndex, IntPtr mappedPtr)
        {
            int index = FindFirstFreeBlockAfter(freeBlocks, block.Offset);
            freeBlocks.Insert(index, block);
            MergeWithNeighbors(freeBlocks, index, memoryTypeIndex, (void*)mappedPtr);
        }

        private static int FindFirstFreeBlockAfter(List<VkMemoryBlock> freeBlocks, ulong offset)
        {
            int low = 0;
            int high = freeBlocks.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (freeBlocks[mid].Offset > offset)
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return low;
        }

        private static void MergeWithNeighbors(List<VkMemoryBlock> freeBlocks, int index, uint memoryTypeIndex, void* mappedPtr)
        {
            if (index > 0 && freeBlocks[index - 1].End == freeBlocks[index].Offset)
            {
                index--;
                MergeWithNext(freeBlocks, index, memoryTypeIndex, mappedPtr);
            }

            if (index + 1 < freeBlocks.Count && freeBlocks[index].End == freeBlocks[index + 1].Offset)
            {
                MergeWithNext(freeBlocks, index, memoryTypeIndex, mappedPtr);
            }
        }

        private static void MergeWithNext(List<VkMemoryBlock> freeBlocks, int index, uint memoryTypeIndex, void* mappedPtr)
        {
            VkMemoryBlock mergedBlock = new VkMemoryBlock(
                freeBlocks[index].DeviceMemory,
                freeBlocks[index].Offset,
                freeBlocks[index + 1].End - freeBlocks[index].Offset,
                memoryTypeIndex,
                mappedPtr,
                false);
            freeBlocks[index] = mergedBlock;
            freeBlocks.RemoveAt(index + 1);
        }
    }
}
