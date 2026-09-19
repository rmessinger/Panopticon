using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Veldrid;

namespace OpenSage.Rendering;

internal sealed class ShaderCacheFile
{
    private const int Version = 2;

    public readonly ShaderDescription VertexShaderDescription;
    public readonly ShaderDescription FragmentShaderDescription;

    public readonly ResourceLayoutDescription[] ResourceLayoutDescriptions;

    public static bool TryLoad(string filePath, [NotNullWhen(true)] out ShaderCacheFile? result)
    {
        if (!File.Exists(filePath))
        {
            result = null;
            return false;
        }

        try
        {
            result = Load(filePath);
            return true;
        }
        catch (IOException)
        {
            // Partially written or unreadable: treat as a cache miss and recompile.
            result = null;
            return false;
        }
    }

    private static ShaderCacheFile Load(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        using var binaryReader = new BinaryReader(fileStream);

        var version = binaryReader.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException("Cache version " + version + " does not match " + Version + ".");
        }

        var vsEntryPoint = binaryReader.ReadString();

        var vsBytesLength = binaryReader.ReadInt32();
        var vsBytes = new byte[vsBytesLength];
        for (var i = 0; i < vsBytesLength; i++)
        {
            vsBytes[i] = binaryReader.ReadByte();
        }

        var fsEntryPoint = binaryReader.ReadString();

        var fsBytesLength = binaryReader.ReadInt32();
        var fsBytes = new byte[fsBytesLength];
        for (var i = 0; i < fsBytesLength; i++)
        {
            fsBytes[i] = binaryReader.ReadByte();
        }

        var numResourceLayoutDescriptions = binaryReader.ReadInt32();
        var resourceLayoutDescriptions = new ResourceLayoutDescription[numResourceLayoutDescriptions];
        for (var i = 0; i < numResourceLayoutDescriptions; i++)
        {
            var numElements = binaryReader.ReadInt32();

            resourceLayoutDescriptions[i] = new ResourceLayoutDescription
            {
                Elements = new ResourceLayoutElementDescription[numElements]
            };

            for (var j = 0; j < numElements; j++)
            {
                resourceLayoutDescriptions[i].Elements[j] = new ResourceLayoutElementDescription
                {
                    Name = binaryReader.ReadString(),
                    Kind = (ResourceKind)binaryReader.ReadByte(),
                    Stages = (ShaderStages)binaryReader.ReadByte(),
                    Options = (ResourceLayoutElementOptions)binaryReader.ReadByte(),
                };
            }
        }

        return new ShaderCacheFile(
            new ShaderDescription(ShaderStages.Vertex, vsBytes, vsEntryPoint),
            new ShaderDescription(ShaderStages.Fragment, fsBytes, fsEntryPoint),
            resourceLayoutDescriptions);
    }

    public ShaderCacheFile(
        in ShaderDescription vertexShaderDescription,
        in ShaderDescription fragmentShaderDescription,
        ResourceLayoutDescription[] resourceLayoutDescriptions)
    {
        VertexShaderDescription = vertexShaderDescription;
        FragmentShaderDescription = fragmentShaderDescription;
        ResourceLayoutDescriptions = resourceLayoutDescriptions;
    }

    public void Save(string filePath)
    {
        // Write to a unique temporary file, then replace atomically, so readers either
        // see the previous complete file or the new one, never a partial write.
        var temporaryPath = filePath + $".tmp{Guid.NewGuid():N}";
        try
        {
            using (var fileStream = File.OpenWrite(temporaryPath))
            {
                using var binaryWriter = new BinaryWriter(fileStream);
                Write(binaryWriter);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void Write(BinaryWriter binaryWriter)
    {
        binaryWriter.Write(Version);

        binaryWriter.Write(VertexShaderDescription.EntryPoint);

        binaryWriter.Write(VertexShaderDescription.ShaderBytes.Length);
        foreach (var value in VertexShaderDescription.ShaderBytes)
        {
            binaryWriter.Write(value);
        }

        binaryWriter.Write(FragmentShaderDescription.EntryPoint);

        binaryWriter.Write(FragmentShaderDescription.ShaderBytes.Length);
        foreach (var value in FragmentShaderDescription.ShaderBytes)
        {
            binaryWriter.Write(value);
        }

        binaryWriter.Write(ResourceLayoutDescriptions.Length);
        for (var i = 0; i < ResourceLayoutDescriptions.Length; i++)
        {
            ref readonly var description = ref ResourceLayoutDescriptions[i];

            binaryWriter.Write(description.Elements.Length);

            for (var j = 0; j < description.Elements.Length; j++)
            {
                ref readonly var element = ref description.Elements[j];

                binaryWriter.Write(element.Name);
                binaryWriter.Write((byte)element.Kind);
                binaryWriter.Write((byte)element.Stages);
                binaryWriter.Write((byte)element.Options);
            }
        }
    }
}
