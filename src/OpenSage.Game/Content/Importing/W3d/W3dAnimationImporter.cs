#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.FileFormats.W3d;
using OpenSage.IO;

namespace OpenSage.Content.Importing.W3d;

/// <summary>
/// W3D reference importer: owns the `hierarchy.animation` key split, W3D path resolution,
/// file parsing, and every W3D channel codec. Produces CPU-only interchange values.
/// </summary>
internal sealed class W3dAnimationImporter : IAnimationImporter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IPathResolver _pathResolver;

    public string Id => "W3D";

    public W3dAnimationImporter(IPathResolver pathResolver)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public ImportedAnimation? ImportAnimation(string key, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var splitName = key.Split('.');

        if (splitName.Length <= 1)
        {
            return null;
        }

        FileSystemEntry? entry = null;
        foreach (var path in _pathResolver.GetPaths(splitName[1], context.Language))
        {
            entry = context.FileSystem.GetFile(path);
            if (entry != null)
            {
                break;
            }
        }

        if (entry == null)
        {
            Logger.Warn("Failed to load animation: " + key);
            return null;
        }

        W3dFile w3dFile;
        using (var entryStream = entry.Open())
        {
            w3dFile = W3dFile.FromStream(entryStream, entry.FilePath);
        }

        var imported = Convert(w3dFile);
        if (imported is null)
        {
            Logger.Warn("Failed to load animation (was null): " + key);
            return null;
        }

        if (!string.Equals(imported.Name, key, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warn("animation name '" + imported.Name + "' does not match '" + key + "'");
        }

        return imported;
    }

    /// <summary>
    /// Shared W3D channel conversion. Returns null when the file contains no animation chunk
    /// (the shipped data contains such references); throws with asset context for unsupported
    /// source data.
    /// </summary>
    internal static ImportedAnimation? Convert(W3dFile w3dFile)
    {
        var w3dAnimations = w3dFile.GetAnimations();
        var w3dCompressedAnimations = w3dFile.GetCompressedAnimations();

        if (w3dAnimations.Count + w3dCompressedAnimations.Count == 0)
        {
            return null;
        }

        if (w3dAnimations.Count + w3dCompressedAnimations.Count > 1)
        {
            throw new NotSupportedException(
                $"Animation file '{w3dFile.FilePath}' contains more than one animation chunk.");
        }

        return w3dAnimations.Count == 1
            ? ConvertUncompressed(w3dAnimations[0])
            : ConvertCompressed(w3dCompressedAnimations[0]);
    }

    private static ImportedAnimation ConvertUncompressed(W3dAnimation w3dAnimation)
    {
        var name = w3dAnimation.Header.HierarchyName + "." + w3dAnimation.Header.Name;
        var duration = TimeSpan.FromSeconds(w3dAnimation.Header.NumFrames / (double)w3dAnimation.Header.FrameRate);

        var channels = new List<ImportedAnimationChannel>();

        foreach (var w3dChannel in w3dAnimation.Channels.OfType<W3dAnimationChannel>())
        {
            if (w3dChannel.ChannelType == W3dAnimationChannelType.UnknownBfme)
            {
                continue;
            }

            channels.Add(ConvertUncompressedChannel(w3dAnimation, w3dChannel));
        }

        foreach (var w3dChannel in w3dAnimation.Channels.OfType<W3dBitChannel>())
        {
            channels.Add(ConvertBitChannel(w3dAnimation, w3dChannel));
        }

        return new ImportedAnimation(name, duration, channels);
    }

    private static ImportedAnimationChannel ConvertUncompressedChannel(
        W3dAnimation w3dAnimation,
        W3dAnimationChannel w3dChannel)
    {
        var keyframes = new ImportedKeyframe[w3dChannel.Data.Length];

        for (var i = 0; i < w3dChannel.Data.Length; i++)
        {
            var time = TimeSpan.FromSeconds(
                (w3dChannel.FirstFrame + i) / (double)w3dAnimation.Header.FrameRate);
            keyframes[i] = CreateKeyframe(w3dChannel.ChannelType, time, w3dChannel.Data[i], w3dAnimation.Header.Name);
        }

        return new ImportedAnimationChannel(
            w3dChannel.Pivot,
            MapChannelType(w3dChannel.ChannelType, w3dAnimation.Header.Name),
            keyframes);
    }

    private static ImportedAnimationChannel ConvertBitChannel(
        W3dAnimation w3dAnimation,
        W3dBitChannel w3dChannel)
    {
        if (w3dChannel.ChannelType != W3dBitChannelType.Visibility)
        {
            throw new NotSupportedException(
                $"Animation '{w3dAnimation.Header.Name}' uses unsupported bit channel type " +
                $"'{w3dChannel.ChannelType}'.");
        }

        var numKeyframes = w3dChannel.Data.Length;
        if (w3dChannel.FirstFrame != 0)
        {
            numKeyframes++;
        }

        if (w3dChannel.LastFrame != w3dAnimation.Header.NumFrames - 1)
        {
            numKeyframes++;
        }

        var keyframes = new ImportedKeyframe[numKeyframes];

        var keyframeIndex = 0;
        if (w3dChannel.FirstFrame != 0)
        {
            keyframes[keyframeIndex++] = ImportedKeyframe.FromBool(TimeSpan.Zero, w3dChannel.DefaultValue);
        }

        for (var i = 0; i < w3dChannel.Data.Length; i++)
        {
            var time = TimeSpan.FromSeconds(
                (w3dChannel.FirstFrame + i) / (double)w3dAnimation.Header.FrameRate);
            keyframes[keyframeIndex++] = ImportedKeyframe.FromBool(time, w3dChannel.Data[i]);
        }

        if (w3dChannel.LastFrame != w3dAnimation.Header.NumFrames - 1)
        {
            var time = TimeSpan.FromSeconds(
                (w3dChannel.LastFrame + 1) / (double)w3dAnimation.Header.FrameRate);
            keyframes[keyframeIndex++] = ImportedKeyframe.FromBool(time, w3dChannel.DefaultValue);
        }

        return new ImportedAnimationChannel(
            w3dChannel.Pivot,
            ImportedAnimationChannelType.Visibility,
            keyframes);
    }

    private static ImportedAnimation ConvertCompressed(W3dCompressedAnimation w3dAnimation)
    {
        var name = w3dAnimation.Header.HierarchyName + "." + w3dAnimation.Header.Name;
        var duration = TimeSpan.FromSeconds(w3dAnimation.Header.NumFrames / (double)w3dAnimation.Header.FrameRate);

        var channels = new List<ImportedAnimationChannel>();

        foreach (var w3dChannel in w3dAnimation.TimeCodedChannels)
        {
            if (w3dChannel.ChannelType == W3dAnimationChannelType.UnknownBfme)
            {
                continue;
            }

            var keyframes = new ImportedKeyframe[w3dChannel.Data.Length];

            for (var i = 0; i < w3dChannel.Data.Length; i++)
            {
                var time = TimeSpan.FromSeconds(
                    w3dChannel.Data[i].TimeCode / (double)w3dAnimation.Header.FrameRate);
                keyframes[i] = CreateKeyframe(w3dChannel.ChannelType, time, w3dChannel.Data[i].Value, w3dAnimation.Header.Name);
            }

            channels.Add(new ImportedAnimationChannel(
                w3dChannel.Pivot,
                MapChannelType(w3dChannel.ChannelType, w3dAnimation.Header.Name),
                keyframes));
        }

        foreach (var w3dChannel in w3dAnimation.AdaptiveDeltaChannels)
        {
            if (w3dChannel.ChannelType == W3dAnimationChannelType.UnknownBfme)
            {
                continue;
            }

            var decodedData = W3dAdaptiveDeltaCodec.Decode(
                w3dChannel.Data,
                w3dChannel.NumTimeCodes,
                w3dChannel.Scale);

            var keyframes = new ImportedKeyframe[decodedData.Length];

            for (var i = 0; i < decodedData.Length; i++)
            {
                var time = TimeSpan.FromSeconds(i / (double)w3dAnimation.Header.FrameRate);
                keyframes[i] = CreateKeyframe(w3dChannel.ChannelType, time, decodedData[i], w3dAnimation.Header.Name);
            }

            channels.Add(new ImportedAnimationChannel(
                w3dChannel.Pivot,
                MapChannelType(w3dChannel.ChannelType, w3dAnimation.Header.Name),
                keyframes));
        }

        foreach (var w3dChannel in w3dAnimation.MotionChannels)
        {
            if (w3dChannel.ChannelType == W3dAnimationChannelType.UnknownBfme)
            {
                continue;
            }

            var keyframes = new ImportedKeyframe[w3dChannel.NumTimeCodes];
            var i = 0;
            foreach (var keyframeWithValue in w3dChannel.Data.GetKeyframesWithValues(w3dChannel))
            {
                var time = TimeSpan.FromSeconds(
                    keyframeWithValue.Keyframe / (double)w3dAnimation.Header.FrameRate);
                keyframes[i++] = CreateKeyframe(w3dChannel.ChannelType, time, keyframeWithValue.Datum, w3dAnimation.Header.Name);
            }

            channels.Add(new ImportedAnimationChannel(
                w3dChannel.Pivot,
                MapChannelType(w3dChannel.ChannelType, w3dAnimation.Header.Name),
                keyframes));
        }

        return new ImportedAnimation(name, duration, channels);
    }

    private static ImportedKeyframe CreateKeyframe(
        W3dAnimationChannelType channelType,
        TimeSpan time,
        in W3dAnimationChannelDatum datum,
        string animationName)
    {
        return channelType switch
        {
            W3dAnimationChannelType.Quaternion => ImportedKeyframe.FromQuaternion(time, datum.Quaternion),

            W3dAnimationChannelType.TranslationX
            or W3dAnimationChannelType.TranslationY
            or W3dAnimationChannelType.TranslationZ => ImportedKeyframe.FromFloat(time, datum.FloatValue),

            _ => throw new NotSupportedException(
                $"Animation '{animationName}' uses unsupported channel type '{channelType}'."),
        };
    }

    private static ImportedAnimationChannelType MapChannelType(
        W3dAnimationChannelType channelType,
        string animationName)
    {
        return channelType switch
        {
            W3dAnimationChannelType.TranslationX => ImportedAnimationChannelType.TranslationX,
            W3dAnimationChannelType.TranslationY => ImportedAnimationChannelType.TranslationY,
            W3dAnimationChannelType.TranslationZ => ImportedAnimationChannelType.TranslationZ,
            W3dAnimationChannelType.Quaternion => ImportedAnimationChannelType.Quaternion,

            _ => throw new NotSupportedException(
                $"Animation '{animationName}' uses unsupported channel type '{channelType}'."),
        };
    }
}
