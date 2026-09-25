using System.Collections.Generic;
using Panopticon.Content;
using Panopticon.Data.Ini;
using Panopticon.Graphics;

namespace Panopticon.Terrain;

[AddedIn(SageGame.Bfme)]
public sealed class WaterTextureList : BaseAsset
{
    internal static WaterTextureList Parse(IniParser parser)
    {
        return parser.ParseNamedBlock(
            (x, name) => x.SetNameAndInstanceId("WaterTextureList", name),
            FieldParseTable);
    }

    private static readonly IniParseTable<WaterTextureList> FieldParseTable = new IniParseTable<WaterTextureList>
    {
        { "Texture", (parser, x) => x.Textures.Add(parser.ParseTextureReference()) }
    };

    public List<LazyAssetReference<TextureAsset>> Textures { get; } = new List<LazyAssetReference<TextureAsset>>();
}
