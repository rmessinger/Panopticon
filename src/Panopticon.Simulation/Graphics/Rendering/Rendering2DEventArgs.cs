using System;
using Panopticon.Gui;

namespace Panopticon.Graphics.Rendering;

public sealed class Rendering2DEventArgs : EventArgs
{
    public DrawingContext2D DrawingContext { get; }

    public Rendering2DEventArgs(DrawingContext2D drawingContext)
    {
        DrawingContext = drawingContext;
    }
}
