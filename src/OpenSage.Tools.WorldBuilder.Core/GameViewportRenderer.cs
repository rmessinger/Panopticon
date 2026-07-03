using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using OpenSage;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Graphics;
using OpenSage.Input;
using OpenSage.Mathematics;
using Veldrid;
using Rectangle = OpenSage.Mathematics.Rectangle;

namespace OpenSage.Tools.WorldBuilder.Core;

public sealed class GameViewportRenderer : IDisposable
{
    private readonly object _inputLock = new();
    private readonly List<InputMessage> _pendingInputMessages = [];

    private Game? _game;
    private Swapchain? _swapchain;
    private TextureCopier? _textureCopier;
    private EditorMap? _editorMap;
    private bool _needsTerrainReload;

    public bool IsInitialized => _game != null && _swapchain != null;

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

    public void Initialize(SwapchainSource source, uint width, uint height)
    {
        if (IsInitialized || Installation == null || width == 0 || height == 0)
        {
            return;
        }

        Width = width;
        Height = height;

        GraphicsBackend? preferredBackend = GetPreferredBackend();
        _game = new Game(Installation, preferredBackend, new Configuration(), null!);

        SwapchainDescription swapchainDesc = new(source, width, height, null, true);
        _swapchain = _game.GraphicsDevice.ResourceFactory.CreateSwapchain(ref swapchainDesc);

        _textureCopier = new TextureCopier(_game, _swapchain.Framebuffer.OutputDescription);
        _game.Panel.EnsureFrame(CreateBounds(width, height));

        if (_editorMap != null)
        {
            LoadMapCore(_editorMap);
        }

        _game.StartRun();
    }

    public void Resize(uint width, uint height)
    {
        if (_swapchain == null || _game == null || width == 0 || height == 0)
        {
            return;
        }

        if (Width == width && Height == height)
        {
            return;
        }

        Width = width;
        Height = height;

        _swapchain.Resize(width, height);
        _game.Panel.EnsureFrame(CreateBounds(width, height));
    }

    public void RenderFrame()
    {
        if (_game == null || _swapchain == null || _textureCopier == null || Width == 0 || Height == 0)
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

        _textureCopier.Execute(
            _game.Panel.Framebuffer.ColorTargets[0].Target,
            _swapchain.Framebuffer);

        _game.GraphicsDevice.SwapBuffers(_swapchain);
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

        _textureCopier?.Dispose();
        _textureCopier = null;

        _swapchain?.Dispose();
        _swapchain = null;

        _game?.Dispose();
        _game = null;
    }

    private void LoadMapCore(EditorMap map)
    {
        if (_game == null || map.MapFile.HeightMapData == null || map.MapFile.SidesList == null || map.MapFile.GlobalLighting == null)
        {
            return;
        }

        _game.TerrainLogic.SetHeightMapData(map.MapFile.HeightMapData);

        Assembly assembly = typeof(Game).Assembly;
        Type gameTypeType = assembly.GetType("OpenSage.GameType", throwOnError: true)!;
        object singlePlayer = Enum.Parse(gameTypeType, "SinglePlayer", ignoreCase: false);

        Type sidesListUtilityType = assembly.GetType("OpenSage.Logic.Map.SidesListUtility", throwOnError: true)!;
        MethodInfo setupGameSidesMethod = sidesListUtilityType.GetMethod(
            "SetupGameSides",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not locate SidesListUtility.SetupGameSides.");

        object?[] sideSetupArguments =
        [
            _game,
            map.MapFile,
            null,
            singlePlayer,
            null,
            null,
            null
        ];

        setupGameSidesMethod.Invoke(null, sideSetupArguments);

        Type playerType = assembly.GetType("OpenSage.Data.Map.Player", throwOnError: true)!;
        Type teamType = assembly.GetType("OpenSage.Data.Map.Team", throwOnError: true)!;
        Type scriptListType = assembly.GetType("OpenSage.Scripting.ScriptList", throwOnError: true)!;

        ConstructorInfo sceneConstructor = typeof(Scene3D).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(IGame),
                typeof(MapFile),
                typeof(string),
                playerType.MakeArrayType(),
                teamType.MakeArrayType(),
                scriptListType.MakeArrayType(),
                gameTypeType
            ],
            modifiers: null)
            ?? throw new InvalidOperationException("Could not locate Scene3D map constructor.");

        string mapPath = map.FilePath ?? "WorldBuilder.map";
        object scene = sceneConstructor.Invoke(
        [
            _game,
            map.MapFile,
            mapPath,
            sideSetupArguments[4]!,
            sideSetupArguments[5]!,
            sideSetupArguments[6]!,
            singlePlayer
        ]);

        _game.Scene3D = (IScene3D)scene;
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
