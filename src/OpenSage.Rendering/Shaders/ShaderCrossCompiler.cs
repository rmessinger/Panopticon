using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Veldrid;
using Veldrid.SPIRV;

namespace OpenSage.Rendering;

internal static class ShaderCrossCompiler
{
    private const string EntryPoint = "main";

    // Increment this whenever the cross-compilation logic changes (e.g. FixMetalMslBindings)
    // so that stale cached shader files are automatically invalidated.
    private const int CrossCompilerVersion = 3;

#if DEBUG
    private const bool UseDebugCompilation = true;
#else
    private const bool UseDebugCompilation = false;
#endif

    public static ShaderCacheFile GetOrCreateCachedShaders(
        ResourceFactory factory,
        Assembly shaderAssembly,
        string shaderName)
    {
        const string shaderCacheFolder = "ShaderCache";
        var backendType = factory.BackendType;
        var targetExtension = backendType.ToString().ToLowerInvariant();

        if (!Directory.Exists(shaderCacheFolder))
        {
            Directory.CreateDirectory(shaderCacheFolder);
        }

        var vsSpvBytes = ReadShaderSpv(shaderAssembly, shaderName, "vert");
        var fsSpvBytes = ReadShaderSpv(shaderAssembly, shaderName, "frag");

        var spvHash = GetShaderHash(vsSpvBytes, fsSpvBytes);

        // Look for cached shader file on disk match the input SPIR-V shaders.
        var cacheFilePath = Path.Combine(shaderCacheFolder, $"OpenSage.Assets.Shaders.{shaderName}.{spvHash}.{targetExtension}");

        if (ShaderCacheFile.TryLoad(cacheFilePath, out var shaderCacheFile))
        {
            // Cache is valid - use it.
            return shaderCacheFile;
        }

        // Cache is invalid or doesn't exist - do cross-compilation.

        // For Vulkan, we don't actually need to do cross-compilation. But we do need to get reflection data.
        // So we cross-compile to HLSL, throw away the resulting HLSL, and use the reflection data.
        var compilationTarget = backendType == GraphicsBackend.Vulkan
            ? CrossCompileTarget.HLSL
            : GetCompilationTarget(backendType);

        var compilationResult = SpirvCompilation.CompileVertexFragment(
            vsSpvBytes,
            fsSpvBytes,
            compilationTarget,
            new CrossCompileOptions());

        byte[] vsBytes, fsBytes;

        switch (backendType)
        {
            case GraphicsBackend.Vulkan:
                vsBytes = vsSpvBytes;
                fsBytes = fsSpvBytes;
                break;

            case GraphicsBackend.Direct3D11:
                vsBytes = CompileHlsl(compilationResult.VertexShader, "vs_5_0");
                fsBytes = CompileHlsl(compilationResult.FragmentShader, "ps_5_0");
                break;

            case GraphicsBackend.OpenGL:
            case GraphicsBackend.OpenGLES:
                vsBytes = Encoding.ASCII.GetBytes(compilationResult.VertexShader);
                fsBytes = Encoding.ASCII.GetBytes(compilationResult.FragmentShader);
                break;

            case GraphicsBackend.Metal:
                // TODO: Compile to IR.
                // SPIRV-cross assigns Metal buffer/texture/sampler indices in a different order
                // than Veldrid's Metal backend (ResourceBindingModel.Improved) expects.
                // Post-process the MSL to remap all [[resource(N)]] indices to match Veldrid.
                var layouts = compilationResult.Reflection.ResourceLayouts;
                vsBytes = Encoding.UTF8.GetBytes(FixMetalMslBindings(compilationResult.VertexShader, layouts, ShaderStages.Vertex));
                fsBytes = Encoding.UTF8.GetBytes(FixMetalMslBindings(compilationResult.FragmentShader, layouts, ShaderStages.Fragment));
                break;

            default:
                throw new InvalidOperationException();
        }

        var entryPoint = factory.BackendType == GraphicsBackend.Metal
            ? $"{EntryPoint}0"
            : EntryPoint;

        shaderCacheFile = new ShaderCacheFile(
            new ShaderDescription(ShaderStages.Vertex, vsBytes, entryPoint),
            new ShaderDescription(ShaderStages.Fragment, fsBytes, entryPoint),
            compilationResult.Reflection.ResourceLayouts);

        shaderCacheFile.Save(cacheFilePath);

        return shaderCacheFile;
    }

    private static byte[] ReadShaderSpv(Assembly assembly, string shaderName, string shaderType)
    {
        var bytecodeShaderName = $"OpenSage.Assets.Shaders.{shaderName}.{shaderType}.spv";
        using (var shaderStream = assembly.GetManifestResourceStream(bytecodeShaderName))
        using (var memoryStream = new MemoryStream())
        {
            shaderStream?.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }

    private static byte[] CompileHlsl(string hlsl, string profile)
    {
        var flags = UseDebugCompilation
            ? Vortice.D3DCompiler.ShaderFlags.Debug
            : Vortice.D3DCompiler.ShaderFlags.OptimizationLevel3;

        var compilationResult = Vortice.D3DCompiler.Compiler.Compile(
            hlsl,
            EntryPoint,
            null!, // digging into the source, this seems to be ok
            profile,
            flags);

        return compilationResult.ToArray();
    }

    private static string GetShaderHash(byte[] vsBytes, byte[] fsBytes)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        sha256.AppendData(vsBytes);
        sha256.AppendData(fsBytes);
        sha256.AppendData(BitConverter.GetBytes(CrossCompilerVersion));

        var hash = sha256.GetCurrentHash();

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }

    private static CrossCompileTarget GetCompilationTarget(GraphicsBackend backend)
    {
        return backend switch
        {
            GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
            GraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
            GraphicsBackend.Metal => CrossCompileTarget.MSL,
            GraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,
            _ => throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}"),
        };
    }

    /// <summary>
    /// Remaps [[buffer(N)]], [[texture(N)]], and [[sampler(N)]] indices in the compiled MSL
    /// function signature to match the binding indices Veldrid's Metal backend will use at runtime.
    ///
    /// SPIRV-cross assigns Metal indices based on actual resource usage in the SPIR-V (e.g.,
    /// VS-exclusive resources first in the VS), while Veldrid computes indices by iterating the
    /// ResourceLayoutDescriptions in order for the given stage. These orderings can differ, causing
    /// shaders to read from the wrong buffers/textures.
    /// </summary>
    private static string FixMetalMslBindings(
        string msl,
        ResourceLayoutDescription[] layouts,
        ShaderStages stage)
    {
        // Compute the Metal buffer/texture/sampler index Veldrid will assign for each named resource.
        // Veldrid's Metal backend (ResourceBindingModel.Improved) assigns indices sequentially per
        // resource type, counting ALL elements across all layouts regardless of shader stage.
        // (MTLResourceLayout.BufferCount / TextureCount / SamplerCount count all elements, and
        // MTLCommandList.GetBufferBase sums those counts. Stage is irrelevant to the slot assignment.)
        var expectedBuffers = new Dictionary<string, int>();
        var expectedTextures = new Dictionary<string, int>();
        var expectedSamplers = new Dictionary<string, int>();

        int bufIdx = 0, texIdx = 0, sampIdx = 0;
        foreach (var layout in layouts)
        {
            foreach (var element in layout.Elements)
            {
                switch (element.Kind)
                {
                    case ResourceKind.UniformBuffer:
                    case ResourceKind.StructuredBufferReadOnly:
                    case ResourceKind.StructuredBufferReadWrite:
                        expectedBuffers[element.Name] = bufIdx++;
                        break;
                    case ResourceKind.TextureReadOnly:
                    case ResourceKind.TextureReadWrite:
                        expectedTextures[element.Name] = texIdx++;
                        break;
                    case ResourceKind.Sampler:
                        expectedSamplers[element.Name] = sampIdx++;
                        break;
                }
            }
        }

        // Find the entry-point function signature to read actual Metal binding indices.
        // [[buffer(N)]], [[texture(N)]], [[sampler(N)]] attributes only appear in function parameters.
        var funcKeyword = stage == ShaderStages.Vertex ? "vertex " : "fragment ";
        var funcStart = msl.IndexOf(funcKeyword, StringComparison.Ordinal);
        if (funcStart < 0)
        {
            return msl;
        }

        var bodyStart = msl.IndexOf('{', funcStart);
        var sig = msl.Substring(funcStart, bodyStart - funcStart);

        // Buffers: "constant TypeName& varName [[buffer(N)]]"
        //       or "const device TypeName& varName [[buffer(N)]]"
        // The TypeName (GLSL uniform block name) matches element.Name in the layout.
        var actualBuffers = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(sig,
            @"(?:constant|const\s+device|device)\s+(\w+)\s*&\s*\w+\s*\[\[buffer\((\d+)\)\]\]"))
        {
            actualBuffers[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        // Textures and samplers: the parameter name before [[texture(N)]] / [[sampler(N)]]
        // matches element.Name in the layout (preserved from GLSL sampler/image variable names).
        var actualTextures = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(sig, @"(\w+)\s*\[\[texture\((\d+)\)\]\]"))
        {
            actualTextures[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        var actualSamplers = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(sig, @"(\w+)\s*\[\[sampler\((\d+)\)\]\]"))
        {
            actualSamplers[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        msl = ApplyMslIndexRemapping(msl, actualBuffers, expectedBuffers, "buffer");
        msl = ApplyMslIndexRemapping(msl, actualTextures, expectedTextures, "texture");
        msl = ApplyMslIndexRemapping(msl, actualSamplers, expectedSamplers, "sampler");

        return msl;
    }

    private static string ApplyMslIndexRemapping(
        string msl,
        Dictionary<string, int> actual,
        Dictionary<string, int> expected,
        string slotType)
    {
        // Build remap: old Metal index → new Metal index (only for resources that moved).
        var remap = new Dictionary<int, int>();
        foreach (var (name, actualIdx) in actual)
        {
            if (expected.TryGetValue(name, out int expectedIdx) && actualIdx != expectedIdx)
            {
                remap[actualIdx] = expectedIdx;
            }
        }

        if (remap.Count == 0)
        {
            return msl;
        }

        // Two-phase replacement to correctly handle index swaps (e.g., 0↔1):
        //   Phase 1: [[kind(N)]] → [[kind(TMP_N)]]  (all remapped indices simultaneously)
        //   Phase 2: [[kind(TMP_N)]] → [[kind(newN)]]
        foreach (var from in remap.Keys)
        {
            msl = msl.Replace($"[[{slotType}({from})]]", $"[[{slotType}(TMP_{from})]]");
        }

        foreach (var (from, to) in remap)
        {
            msl = msl.Replace($"[[{slotType}(TMP_{from})]]", $"[[{slotType}({to})]]");
        }

        return msl;
    }
}
