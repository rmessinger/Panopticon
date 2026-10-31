// BitKnit2 quantum decompressor. Original C# written from the libbg3 written
// specification (github.com/eiz/libbg3@23c701a, MIT); cross-checked against
// pybg3 rans.h (github.com/eiz/pybg3@3b02e59, MIT, Copyright 2024 Mackenzie
// Straight). No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Decodes BitKnit2 quantums into the output buffer: 64 KiB quanta, a pair of
/// interleaved rANS states initialized per quantum, four command and
/// cache-reference models selected by destination offset modulo 4, delta
/// literals against the last copy offset, and an 8-entry offset cache.
/// </summary>
internal sealed class BitKnit2Decompressor
{
    private const int QuantumSize = 0x10000;
    private const int CommandVocab = 300;
    private const int CommandMinProbable = 36;
    private const int CacheReferenceVocab = 40;
    private const int OffsetVocab = 21;
    private const int AdaptationInterval = 1024;

    private readonly byte[] _output;
    private readonly BitKnit2AdaptiveModel[] _commandModels = new BitKnit2AdaptiveModel[4];
    private readonly BitKnit2AdaptiveModel[] _cacheReferenceModels = new BitKnit2AdaptiveModel[4];
    private readonly BitKnit2AdaptiveModel _offsetModel;
    private readonly BitKnit2OffsetCache _offsetCache = new();
    private int _position;
    private int _deltaOffset = 1;

    public BitKnit2Decompressor(byte[] output)
    {
        _output = output;
        _offsetModel = new BitKnit2AdaptiveModel(AdaptationInterval, OffsetVocab, 0, 15, 10);
        for (var i = 0; i < 4; i++)
        {
            _commandModels[i] = new BitKnit2AdaptiveModel(AdaptationInterval, CommandVocab, CommandMinProbable, 15, 10);
            _cacheReferenceModels[i] = new BitKnit2AdaptiveModel(AdaptationInterval, CacheReferenceVocab, 0, 15, 10);
        }
    }

    public void Run(ref BitKnit2WordStream stream)
    {
        while (_position < _output.Length)
        {
            DecodeQuantum(ref stream);
        }
    }

    private void DecodeQuantum(ref BitKnit2WordStream stream)
    {
        var boundary = Math.Min(_output.Length, (_position & ~0xFFFF) + QuantumSize);
        if (stream.Peek() == 0)
        {
            // A zero word marks an uncompressed quantum: the remaining words
            // are raw output bytes.
            _ = stream.Pop();
            var copyLength = Math.Min(stream.RemainingBytes.Length, boundary - _position);
            stream.RemainingBytes[..copyLength].CopyTo(_output.AsSpan(_position));
            _position += copyLength;
            stream.AdvanceBytes(copyLength);
            return;
        }

        var state1 = default(BitKnit2RansState);
        var state2 = default(BitKnit2RansState);
        DecodeInitialState(ref stream, ref state1, ref state2);
        if (_position == 0)
        {
            // The first output byte has no delta base and comes from raw bits.
            _output[0] = (byte)PopBits(ref stream, ref state1, ref state2, 8);
            _position = 1;
        }

        while (_position < boundary)
        {
            var context = _position % 4;
            var command = (int)PopModel(ref stream, _commandModels[context], ref state1, ref state2);
            if (command >= 256)
            {
                DecodeCopy(ref stream, command, ref state1, ref state2);
            }
            else
            {
                _output[_position] = (byte)(command + _output[_position - _deltaOffset]);
                _position++;
            }
        }

        if (state1.Bits != BitKnit2RansState.RefillThreshold
            || state2.Bits != BitKnit2RansState.RefillThreshold)
        {
            throw new InvalidDataException("BitKnit2 quantum ended with an unflushed rANS state.");
        }
    }

    private void DecodeCopy(ref BitKnit2WordStream stream, int command, ref BitKnit2RansState state1, ref BitKnit2RansState state2)
    {
        var context = _position % 4;
        int copyLength;
        if (command < 288)
        {
            copyLength = command - 254;
        }
        else
        {
            var extraBits = command - 287;
            var extraValue = PopBits(ref stream, ref state1, ref state2, extraBits);
            copyLength = (1 << extraBits) + (int)extraValue + 32;
        }

        uint offset;
        var cacheReference = (int)PopModel(ref stream, _cacheReferenceModels[context], ref state1, ref state2);
        if (cacheReference < 8)
        {
            offset = _offsetCache.Hit(cacheReference);
        }
        else
        {
            var offsetLength = (int)PopModel(ref stream, _offsetModel, ref state1, ref state2);
            var offsetBits = PopBits(ref stream, ref state1, ref state2, offsetLength % 16);
            if (offsetLength >= 16)
            {
                offsetBits = (offsetBits << 16) | stream.Pop();
            }

            offset = (32u << offsetLength) + (offsetBits << 5) - 32u + (uint)(cacheReference - 7);
            _offsetCache.Insert(offset);
        }

        if (offset > (uint)_position)
        {
            throw new InvalidDataException(
                $"BitKnit2 copy offset {offset} reaches before the {_position}-byte output start.");
        }

        if (copyLength > _output.Length - _position)
        {
            throw new InvalidDataException(
                $"BitKnit2 copy length {copyLength} exceeds the declared output size {_output.Length}.");
        }

        _deltaOffset = (int)offset;
        for (var i = 0; i < copyLength; i++)
        {
            _output[_position] = _output[_position - (int)offset];
            _position++;
        }
    }

    private static void DecodeInitialState(ref BitKnit2WordStream stream, ref BitKnit2RansState state1, ref BitKnit2RansState state2)
    {
        // The quantum's final rANS states are stored up front ("tying the
        // knot"): a merged value split into the two initial states.
        var merged = new BitKnit2RansState(((uint)stream.Pop() << 16) | stream.Pop());
        var split = (int)merged.PopBits(ref stream, 4);
        state1.Bits = merged.Bits >> split;
        state1.MaybeRefill(ref stream);
        state2.Bits = (merged.Bits << 16) | stream.Pop();
        state2.Bits &= (1u << (16 + split)) - 1;
        state2.Bits |= 1u << (16 + split);
    }

    private static uint PopBits(ref BitKnit2WordStream stream, ref BitKnit2RansState state1, ref BitKnit2RansState state2, int bitCount)
    {
        var result = state1.PopBits(ref stream, bitCount);
        Swap(ref state1, ref state2);
        return result;
    }

    private static uint PopModel(ref BitKnit2WordStream stream, BitKnit2AdaptiveModel model, ref BitKnit2RansState state1, ref BitKnit2RansState state2)
    {
        var result = state1.PopCdf(ref stream, model.Cdf);
        model.ObserveSymbol((int)result);
        Swap(ref state1, ref state2);
        return result;
    }

    private static void Swap(ref BitKnit2RansState left, ref BitKnit2RansState right)
    {
        var temp = left;
        left = right;
        right = temp;
    }
}
