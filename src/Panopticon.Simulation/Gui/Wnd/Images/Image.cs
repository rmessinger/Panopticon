using System;
using Panopticon.Mathematics;
using Veldrid;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Gui.Wnd.Images;

public sealed class Image
{
    private readonly ImageSource _source;
    private readonly bool _grayscale;
    private Texture _texture;
    private Size _size;

    public string Name { get; }

    public Size NaturalSize => _source.NaturalSize;

    /// <summary>
    /// Chrome classification for dynamic HUD drawing (WI-G9-03). Anything other than
    /// <see cref="HudChromeKind.None"/> draws procedurally instead of sampling the texture
    /// while the active preset selects dynamic HUD chrome.
    /// </summary>
    public HudChromeKind ChromeKind { get; set; } = HudChromeKind.None;

    internal Image(string name, ImageSource source, bool grayscale = false)
    {
        Name = name;
        _source = source;
        _grayscale = grayscale;
    }

    internal void SetSize(in Size size)
    {
        var actualSize = _source.NaturalSize.Width > size.Width
            ? _source.NaturalSize
            : size;

        if (_size == actualSize)
        {
            return;
        }

        _texture = _source.GetTexture(actualSize);
        if (size.Width == 0 || size.Height == 0)
        {
            return;
        }

        if (_texture == null)
        {
            throw new InvalidOperationException();
        }

        _size = actualSize;
    }

    public Image WithGrayscale(bool grayscale)
    {
        return new Image(Name, _source, grayscale) { ChromeKind = ChromeKind };
    }

    internal void Draw(DrawingContext2D drawingContext, in Rectangle destinationRect)
    {
        if (ChromeKind != HudChromeKind.None && drawingContext.DynamicHudChrome)
        {
            HudChromeAudit.DynamicChromePaints++;
            HudChromePainter.DrawChrome(drawingContext, destinationRect, ChromeKind);
            return;
        }

        if (_texture == null)
        {
            // TODO: crashes in multiplayer mode
            return;
            // throw new InvalidOperationException();
        }

        if (ChromeKind != HudChromeKind.None)
        {
            HudChromeAudit.ChromeBitmapDraws++;
        }

        drawingContext.DrawImage(_texture, null, destinationRect, grayscale: _grayscale);
    }
}
