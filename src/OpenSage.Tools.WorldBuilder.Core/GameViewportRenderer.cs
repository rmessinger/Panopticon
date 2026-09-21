using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using OpenSage;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Graphics;
using OpenSage.Graphics.Rendering;
using OpenSage.Input;
using OpenSage.Logic.Map;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Terrain;
using Veldrid;
using Rectangle = OpenSage.Mathematics.Rectangle;

namespace OpenSage.Tools.WorldBuilder.Core;

public sealed class GameViewportRenderer : IDisposable
{
    private readonly object _inputLock = new();
    private readonly List<InputMessage> _pendingInputMessages = [];

    private Game? _game;
    private EditorMap? _editorMap;
    private bool _needsTerrainReload;
    private long _frameVersion;

    public bool IsInitialized => _game != null;

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public required GameInstallation? Installation { get; set; }

    /// <summary>
    /// The currently active editor tool. Pointer events are forwarded to this tool
    /// after terrain picking resolves the world position.
    /// </summary>
    public IEditorTool? ActiveTool { get; set; }

    // ── View toggles read by RenderFrame each tick ─────────────────────────
    public bool ShowGrid { get; set; } = true;
    public bool ShowObjects { get; set; } = true;
    public bool ShowWater { get; set; } = true;
    public bool ShowWaypoints { get; set; } = true;
    public bool ShowPassability { get; set; }
    public bool IsLockAngle { get; set; }
    public bool IsTopDown { get; set; }

    /// <summary>
    /// Presenting front-ends increment this after presenting a frame; callers can
    /// poll it to avoid re-reading an unchanged frame.
    /// </summary>
    public long FrameVersion => _frameVersion;

    /// <summary>
    /// The offscreen color target of the last rendered frame. Presentation
    /// (native swapchain or CPU readback) is owned by the front-end.
    /// </summary>
    public Veldrid.Texture? FrameTexture =>
        _game?.Panel.Framebuffer.ColorTargets[0].Target;

    public Veldrid.GraphicsDevice? GraphicsDevice => _game?.GraphicsDevice;

    /// <summary>
    /// Creates the offscreen game. Frames render into the panel framebuffer;
    /// the front-end presents them however its UI toolkit requires.
    /// </summary>
    public void Initialize(uint width, uint height)
    {
        if (IsInitialized || Installation == null || width == 0 || height == 0)
        {
            return;
        }

        Width = width;
        Height = height;

        GraphicsBackend? preferredBackend = GetPreferredBackend();
        _game = new Game(installation: Installation, preferredBackend, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null!);

        _game.Panel.EnsureFrame(CreateBounds(width, height));

        if (_editorMap != null)
        {
            LoadMapCore(_editorMap);
        }

        _game.StartRun();
    }

    public void Resize(uint width, uint height)
    {
        if (_game == null || width == 0 || height == 0)
        {
            return;
        }

        if (Width == width && Height == height)
        {
            return;
        }

        Width = width;
        Height = height;

        _game.Panel.EnsureFrame(CreateBounds(width, height));
    }

    public void RenderFrame()
    {
        // Game.Update requires a scene (logic ticks read GameEngine from it);
        // before a map is loaded there is nothing to render.
        if (_game == null || _game.Scene3D is null || Width == 0 || Height == 0)
        {
            return;
        }

        // Reload terrain visual after a tool stroke modified the height map
        if (_needsTerrainReload && _editorMap != null)
        {
            _needsTerrainReload = false;
            _game.TerrainLogic.SetHeightMapData(_editorMap.MapFile.HeightMapData);
        }

        InputMessage[] inputMessages;
        lock (_inputLock)
        {
            inputMessages = _pendingInputMessages.ToArray();
            _pendingInputMessages.Clear();
        }

        _game.Update(inputMessages);
        _game.Panel.EnsureFrame(CreateBounds(Width, Height));
        _game.Render();
        _frameVersion++;
    }

    public void OnPointerEvent(ViewportPointerEvent pointerEvent)
    {
        Point2D position = new(
            (int)MathF.Round(pointerEvent.Position.X),
            (int)MathF.Round(pointerEvent.Position.Y));

        switch (pointerEvent.EventType)
        {
            case ViewportPointerEventType.Down:
                QueueInputMessage(InputMessage.CreateMouseMove(position));
                QueueInputMessage(InputMessage.CreateMouseButton(GetMouseButtonDown(pointerEvent.Button), position));
                break;

            case ViewportPointerEventType.Move:
                QueueInputMessage(InputMessage.CreateMouseMove(position));
                break;

            case ViewportPointerEventType.Up:
                QueueInputMessage(InputMessage.CreateMouseMove(position));
                QueueInputMessage(InputMessage.CreateMouseButton(GetMouseButtonUp(pointerEvent.Button), position));
                break;

            case ViewportPointerEventType.Scroll:
                QueueInputMessage(InputMessage.CreateMouseMove(position));
                QueueInputMessage(InputMessage.CreateMouseWheel((int)MathF.Round(pointerEvent.ScrollDelta)));
                break;
        }

        // Resolve world position and forward to active tool
        if (ActiveTool != null && _editorMap != null && pointerEvent.EventType != ViewportPointerEventType.Scroll)
        {
            pointerEvent.WorldPosition = PickTerrainPoint(pointerEvent.Position.X, pointerEvent.Position.Y);

            switch (pointerEvent.EventType)
            {
                case ViewportPointerEventType.Down:
                    ActiveTool.OnPointerDown(pointerEvent, _editorMap);
                    break;

                case ViewportPointerEventType.Move:
                    ActiveTool.OnPointerMove(pointerEvent, _editorMap);
                    break;

                case ViewportPointerEventType.Up:
                    ActiveTool.OnPointerUp(pointerEvent, _editorMap);
                    _needsTerrainReload = true;
                    break;
            }
        }
    }

    /// <summary>
    /// Casts a ray from screen position (x, y) into the scene and returns the world-space
    /// terrain intersection point, or null if no scene or terrain is loaded.
    /// </summary>
    public Vector3? PickTerrainPoint(float screenX, float screenY)
    {
        var scene = _game?.Scene3D;
        if (scene is null)
        {
            return null;
        }

        var ray = scene.Camera.ScreenPointToRay(new Vector2(screenX, screenY));
        return scene.Terrain?.Intersect(ray);
    }

    public void OnKeyEvent(ViewportKeyEvent keyEvent)
    {
        if (!TryMapKey(keyEvent.Key, out Key key))
        {
            return;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        if (keyEvent.Control)
        {
            modifiers |= ModifierKeys.Control;
        }

        if (keyEvent.Shift)
        {
            modifiers |= ModifierKeys.Shift;
        }

        QueueInputMessage(keyEvent.IsDown
            ? InputMessage.CreateKeyDown(key, modifiers)
            : InputMessage.CreateKeyUp(key, modifiers));
    }

    public void LoadMap(EditorMap map)
    {
        _editorMap = map ?? throw new ArgumentNullException(nameof(map));

        if (_game == null)
        {
            return;
        }

        LoadMapCore(map);
    }

    public void Dispose()
    {
        lock (_inputLock)
        {
            _pendingInputMessages.Clear();
        }

        _game?.Dispose();
        _game = null;
    }

    /// <summary>
    /// Raised with a human-readable status line when a map load succeeds or
    /// is refused; the UI relays it to the status bar.
    /// </summary>
    public event Action<string>? LoadStatus;

    private void LoadMapCore(EditorMap map)
    {
        if (_game == null || map.MapFile.HeightMapData == null || map.MapFile.SidesList == null || map.MapFile.GlobalLighting == null)
        {
            LoadStatus?.Invoke("Map lacks height, sides, or lighting data; no 3D preview.");
            return;
        }

        // Load through the game's own single-player flow: it performs the full
        // StartGame sequence (sides setup, scene construction, seeded randoms,
        // network buffer, camera framing, culling) that the render pipeline
        // expects. Maps outside the game file system are refused for the 3D
        // preview rather than constructed ad hoc, which crashes GPU drivers;
        // mounting editor maps into a composite file system is follow-up work.
        if (map.FilePath is { } mapPath && _game.ContentManager.GetMapEntry(mapPath) != null)
        {
            _game.StartSinglePlayerGame(mapPath);
            FrameCameraOnMap(map);
            LoadStatus?.Invoke($"3D preview: {Path.GetFileName(mapPath)}");
        }
        else
        {
            LoadStatus?.Invoke(
                "3D preview requires the map to live inside the game installation; " +
                "editing still works, the viewport stays blank.");
        }
    }

    private void FrameCameraOnMap(EditorMap map)
    {
        // The tactical camera starts unpositioned; aim it at the map centre so
        // the viewport shows terrain immediately instead of empty space.
        var heightMap = map.MapFile.HeightMapData!;
        var centerX = heightMap.Width * HeightMap.HorizontalScale / 2f;
        var centerY = heightMap.Height * HeightMap.HorizontalScale / 2f;
        _game!.Scene3D!.TacticalView.LookAt(new Vector3(centerX, centerY, 0));
        _game.Scene3D.TacticalView.InitHeightForMap();
        _game.Scene3D.TacticalView.SetAngleAndPitchToDefault();
        _game.Scene3D.TacticalView.SetZoomToDefault();
    }

    private void QueueInputMessage(InputMessage inputMessage)
    {
        lock (_inputLock)
        {
            _pendingInputMessages.Add(inputMessage);
        }
    }

    private static Rectangle CreateBounds(uint width, uint height)
    {
        return new Rectangle(0, 0, (int)width, (int)height);
    }

    private static GraphicsBackend? GetPreferredBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return GraphicsBackend.Direct3D11;
        }

        if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS())
        {
            return GraphicsBackend.Metal;
        }

        return null;
    }

    private static InputMessageType GetMouseButtonDown(ViewportMouseButton button)
    {
        return button switch
        {
            ViewportMouseButton.Left => InputMessageType.MouseLeftButtonDown,
            ViewportMouseButton.Middle => InputMessageType.MouseMiddleButtonDown,
            ViewportMouseButton.Right => InputMessageType.MouseRightButtonDown,
            _ => throw new InvalidOperationException("Pointer down event requires a mouse button.")
        };
    }

    private static InputMessageType GetMouseButtonUp(ViewportMouseButton button)
    {
        return button switch
        {
            ViewportMouseButton.Left => InputMessageType.MouseLeftButtonUp,
            ViewportMouseButton.Middle => InputMessageType.MouseMiddleButtonUp,
            ViewportMouseButton.Right => InputMessageType.MouseRightButtonUp,
            _ => throw new InvalidOperationException("Pointer up event requires a mouse button.")
        };
    }

    private static bool TryMapKey(string keyText, out Key key)
    {
        switch (keyText)
        {
            case "ArrowLeft":
                key = Key.Left;
                return true;

            case "ArrowRight":
                key = Key.Right;
                return true;

            case "ArrowUp":
                key = Key.Up;
                return true;

            case "ArrowDown":
                key = Key.Down;
                return true;

            case "Control":
            case "Ctrl":
                key = Key.ControlLeft;
                return true;

            case "Shift":
                key = Key.ShiftLeft;
                return true;

            case "Alt":
                key = Key.AltLeft;
                return true;
        }

        string normalized = keyText.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse(normalized, ignoreCase: true, out key))
        {
            return true;
        }

        if (normalized.Length == 1 && Enum.TryParse(normalized.ToUpperInvariant(), ignoreCase: false, out key))
        {
            return true;
        }

        if (int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out int digit) &&
            Enum.TryParse($"Number{digit}", ignoreCase: false, out key))
        {
            return true;
        }

        key = default;
        return false;
    }
}
