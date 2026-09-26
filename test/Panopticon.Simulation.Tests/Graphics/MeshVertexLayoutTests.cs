using System;
using System.Linq;
using System.Runtime.InteropServices;
using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class MeshVertexLayoutTests
{
    [Fact]
    public void BasicVertexIsOneHundredFourBytes()
    {
        Assert.Equal(104, Marshal.SizeOf<MeshShaderResources.MeshVertex.Basic>());
    }

    [Fact]
    public void BasicLayoutMatchesStructFieldCount()
    {
        var basicLayout = MeshShaderResources.MeshVertex.VertexDescriptors[0];

        Assert.Equal(14, basicLayout.Elements.Count());
        Assert.Equal(
            Marshal.SizeOf<MeshShaderResources.MeshVertex.Basic>(),
            basicLayout.Elements.Sum(element => FormatSize(element.Format)));
    }

    [Fact]
    public void TexCoordLayoutMatchesStructSize()
    {
        var texCoordLayout = MeshShaderResources.MeshVertex.VertexDescriptors[1];

        Assert.Equal(2, texCoordLayout.Elements.Count());
        Assert.Equal(
            Marshal.SizeOf<MeshShaderResources.MeshVertex.TexCoords>(),
            texCoordLayout.Elements.Sum(element => FormatSize(element.Format)));
    }

    private static int FormatSize(VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float1 => 4,
            VertexElementFormat.Float2 => 8,
            VertexElementFormat.Float3 => 12,
            VertexElementFormat.UInt1 => 4,
            _ => throw new NotSupportedException($"Unexpected vertex element format {format}."),
        };
    }
}
