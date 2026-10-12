using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using Panopticon.Gui;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Per-frame state the overlay renders. <see cref="HoldingOrders"/> shows the resume control
/// (the scenario is paused at its starting conditions); otherwise the pause toggle and progress.
/// </summary>
public sealed record ValidationGroundOverlayState(
    bool HoldingOrders,
    bool Paused,
    int ResolvedUnits,
    int TotalUnits,
    int Gaps);

/// <summary>
/// Shared windowed performance overlay, with optional validation-ground status and hover details.
/// Validation controls own Space and F1 regardless of status-window visibility.
/// </summary>
public sealed class HarnessOverlay : IDisposable
{
    private readonly ImGuiRenderer _imgui;
    private readonly CommandList _commandList;
    private readonly GameWindow? _window;
    private readonly Game _game;
    private readonly Action _unsubscribeResize;

    private bool _spaceDown;
    private bool _f1Down;
    private bool _statusWindowVisible;
    private bool _performanceExpanded = true;
    private readonly bool _validationControls;
    private float _performanceHeight;
    private long _objectSampleTimestamp;
    private int _liveObjectCount;

    public HarnessPerformanceMetrics Performance { get; } = new();
    internal long PreviousFrameTimestamp { get; set; }

    private static readonly Vector2? HoverPosition = ParseHoverPosition(
        Environment.GetEnvironmentVariable("TESTHARNESS_VG_HOVER"));

    private static readonly string? HoverObjectDefinition =
        Environment.GetEnvironmentVariable("TESTHARNESS_VG_HOVER_OBJECT");

    private static Vector2? ParseHoverPosition(string? value)
    {
        if (value == null)
        {
            return null;
        }

        var parts = value.Split(',');
        return parts.Length == 2
            && float.TryParse(parts[0], out var x)
            && float.TryParse(parts[1], out var y)
            ? new Vector2(x, y)
            : null;
    }

    public ValidationGroundOverlayState State { get; set; } = new(false, false, 0, 0, 0);

    /// <summary>Set for one frame when the button or Space activates the current primary action
    /// (resume-from-hold while held, pause toggle afterwards). The runner consumes it.</summary>
    public bool PrimaryActionRequested { get; private set; }

    /// <summary>True when ImGui wants the mouse this frame; the runner drops mouse messages then.</summary>
    public bool WantCaptureMouse { get; private set; }

    public HarnessOverlay(Game game, GameWindow window, bool validationControls = false)
    {
        _validationControls = validationControls;
        _game = game;
        _window = window;
        _imgui = new ImGuiRenderer(
            game.GraphicsDevice,
            window.Swapchain.Framebuffer.OutputDescription,
            window.DrawableBounds.Width,
            window.DrawableBounds.Height);

        void OnWindowSizeChanged(object? sender, EventArgs e)
        {
            _imgui.WindowResized(window.DrawableBounds.Width, window.DrawableBounds.Height, window.DpiScale);
        }

        OnWindowSizeChanged(this, EventArgs.Empty);
        window.ClientSizeChanged += OnWindowSizeChanged;
        _unsubscribeResize = () => window.ClientSizeChanged -= OnWindowSizeChanged;

        _commandList = game.GraphicsDevice.ResourceFactory.CreateCommandList();
    }

    /// <summary>
    /// Windowless construction for offscreen overlay screenshots (docs/screenshots.md): renders
    /// into the game panel's framebuffer instead of a window swapchain. The output description
    /// must come from the panel framebuffer after <c>EnsureFrame</c>, not from
    /// <see cref="Graphics.Rendering.RenderPipeline.GameOutputDescription"/>, or the ImGui
    /// pipeline mismatches the framebuffer at draw time.
    /// </summary>
    internal HarnessOverlay(Game game, Veldrid.OutputDescription outputDescription, int width, int height)
    {
        _validationControls = true;
        _game = game;
        _imgui = new ImGuiRenderer(
            game.GraphicsDevice,
            outputDescription,
            width,
            height);

        _unsubscribeResize = () => { };

        _commandList = game.GraphicsDevice.ResourceFactory.CreateCommandList();
    }

    public void Update(float deltaSeconds, InputSnapshot snapshot)
    {
        PrimaryActionRequested = _validationControls && DetectSpace(snapshot);
        if (_validationControls && DetectKeyToggle(snapshot, Key.F1, ref _f1Down))
        {
            _statusWindowVisible = !_statusWindowVisible;
        }

        _imgui.Update(deltaSeconds, snapshot);

        if (_validationControls)
        {
            InjectSyntheticHover();
        }

        DrawPerformanceWindow();

        if (_statusWindowVisible)
        {
            DrawStatusWindow();
        }

        if (_validationControls)
        {
            DrawObjectTooltip();
        }

        WantCaptureMouse = ImGui.GetIO().WantCaptureMouse;
    }

    private void DrawPerformanceWindow()
    {
        if (_objectSampleTimestamp == 0 || Stopwatch.GetElapsedTime(_objectSampleTimestamp).TotalSeconds >= 1)
        {
            _liveObjectCount = _game.Scene3D?.GameObjects.Objects.Count() ?? 0;
            _objectSampleTimestamp = Stopwatch.GetTimestamp();
        }

        ImGui.SetNextWindowPos(new Vector2(16, 16), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.85f);
        ImGui.Begin("##harness-performance", ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoFocusOnAppearing);
        if (ImGui.SmallButton(_performanceExpanded ? "-" : "+"))
        {
            _performanceExpanded = !_performanceExpanded;
        }

        ImGui.SameLine();
        ImGui.Text("Performance");
        if (_performanceExpanded)
        {
            var metrics = Performance;
            ImGui.Text(_game.IsLogicRunning ? "Running" : "Paused");
            if (metrics.FramesPerSecond == 0)
            {
                ImGui.TextDisabled("Collecting first timing sample...");
            }
            else
            {
                ImGui.Text($"FPS {metrics.FramesPerSecond:F1} | frame {metrics.FrameMilliseconds:F2} ms");
            }
            ImGui.Text($"CPU update {metrics.UpdateMilliseconds:F2} ms");
            ImGui.Text($"CPU render submission {metrics.RenderMilliseconds:F2} ms");
            ImGui.Text($"Process {metrics.WorkingSetBytes / 1048576d:F1} MiB");
            ImGui.Text($"Managed heap {metrics.ManagedBytes / 1048576d:F1} MiB");
            ImGui.Text($"GC collections {metrics.Gen0Collections} / {metrics.Gen1Collections} / {metrics.Gen2Collections}");
            ImGui.Text($"Live objects {_liveObjectCount}");
            ImGui.TextDisabled("1 s averages | CPU timings, not GPU");
        }

        _performanceHeight = ImGui.GetWindowSize().Y;
        ImGui.End();
    }

    /// <summary>
    /// Synthetic hover for screenshot capture (docs/screenshots.md): TESTHARNESS_VG_HOVER=x,y
    /// pins the mouse at a screen position, TESTHARNESS_VG_HOVER_OBJECT=&lt;definition&gt; pins
    /// it on the first object with that definition (recomputed each frame, so it follows camera
    /// motion). Assigned directly to <c>io.MousePos</c> after the renderer's NewFrame consumed
    /// the (windowless Xvfb: fixed) snapshot position, so the tooltip renders for capture.
    /// </summary>
    private void InjectSyntheticHover()
    {
        var scene = _game.Scene3D;
        if (scene == null)
        {
            return;
        }

        var io = ImGui.GetIO();
        if (HoverObjectDefinition != null)
        {
            foreach (var gameObject in scene.GameObjects.Objects)
            {
                if (gameObject.Definition.Name != HoverObjectDefinition)
                {
                    continue;
                }

                var screen = scene.Camera.WorldToScreenPoint(gameObject.Transform.Translation);
                if (screen.Z < 0 || screen.Z > 1)
                {
                    continue;
                }

                io.MousePos = new Vector2(
                    Math.Clamp(screen.X, 0, io.DisplaySize.X - 1),
                    Math.Clamp(screen.Y, 0, io.DisplaySize.Y - 1));
                return;
            }

            return;
        }

        if (HoverPosition != null)
        {
            io.MousePos = HoverPosition.Value;
        }
    }

    private void DrawStatusWindow()
    {
        var state = State;
        ImGui.SetNextWindowPos(new Vector2(16, 24 + _performanceHeight), ImGuiCond.Always, Vector2.Zero);
        ImGui.Begin("Validation Ground", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);

        if (state.HoldingOrders)
        {
            ImGui.Text("Paused at start - inspect the formation, then resume.");
            ImGui.Text($"{state.TotalUnits} units ({state.TotalUnits - state.Gaps} measurable, {state.Gaps} gaps)");
            if (ImGui.Button("Resume: Attack Targets (Space)", new Vector2(240, 0)))
            {
                PrimaryActionRequested = true;
            }
        }
        else
        {
            ImGui.Text(state.Paused ? "PAUSED" : "Running");
            ImGui.Text($"resolved {state.ResolvedUnits} / {state.TotalUnits}, {state.Gaps} gaps");
            if (ImGui.Button(state.Paused ? "Resume (Space)" : "Pause (Space)", new Vector2(240, 0)))
            {
                PrimaryActionRequested = true;
            }
        }

        ImGui.TextDisabled("F1 hides this window. Hover a unit for details.");

        ImGui.End();
    }

    private void DrawObjectTooltip()
    {
        var hovered = FindHoveredObject();
        if (hovered == null)
        {
            return;
        }

        // Docked at the right edge above the bottom HUD (the control bar occupies roughly the
        // bottom fifth of the screen), independent of mouse position; only the trigger is
        // hover-based. Pivot (1,0) anchors the window's top-right corner.
        var display = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(
            new Vector2(display.X - 12, display.Y * 0.80f),
            ImGuiCond.Always,
            new Vector2(1, 0));
        ImGui.SetNextWindowBgAlpha(0.85f);
        ImGui.Begin(
            "##object-tooltip",
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav);

        var owner = hovered.Owner;
        var ownerColor = owner != null
            ? new Vector4(owner.Color.R / 255f, owner.Color.G / 255f, owner.Color.B / 255f, 1f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1f);

        ImGui.TextColored(ownerColor, owner?.DisplayName ?? owner?.Name ?? "Neutral / Civilian");
        ImGui.Separator();

        ImGui.Text(hovered.Definition.DisplayName ?? hovered.Definition.Name);
        ImGui.TextDisabled(hovered.Definition.Name);

        if (hovered.BodyModule != null)
        {
            var health = hovered.BodyModule.Health;
            var maxHealth = hovered.BodyModule.MaxHealth;
            var fraction = maxHealth > 0 ? health / maxHealth : 0f;
            ImGui.Spacing();
            ImGui.Text($"HP {health:0}/{maxHealth:0}");
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, healthFractionColor(fraction));
            ImGui.ProgressBar(fraction, new Vector2(180, 8), string.Empty);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        var translation = hovered.Transform.Translation;
        ImGui.TextDisabled($"id {hovered.Id.Index}  pos ({translation.X:0}, {translation.Y:0}, {translation.Z:0})");

        ImGui.End();

        static Vector4 healthFractionColor(float fraction) => fraction switch
        {
            > 0.6f => new Vector4(0.25f, 0.8f, 0.3f, 1f),
            > 0.3f => new Vector4(0.9f, 0.75f, 0.2f, 1f),
            _ => new Vector4(0.85f, 0.25f, 0.25f, 1f),
        };
    }

    /// <summary>
    /// Closest object whose collider is hit by the ray through the mouse cursor, preferring
    /// non-structures over structures like the engine's selection pick. Skips the pick while
    /// the status window owns the mouse so window interactions do not fight the tooltip.
    /// </summary>
    private Panopticon.Logic.Object.GameObject? FindHoveredObject()
    {
        var scene = _game.Scene3D;
        if (scene == null || ImGui.GetIO().WantCaptureMouse)
        {
            return null;
        }

        var mouse = ImGui.GetIO().MousePos;
        var ray = scene.Camera.ScreenPointToRay(new Vector2(mouse.X, mouse.Y));

        var closestDepth = float.MaxValue;
        Panopticon.Logic.Object.GameObject? closest = null;

        foreach (var gameObject in scene.GameObjects.Objects)
        {
            if (gameObject.RoughCollider == null ||
                !gameObject.RoughCollider.Intersects(ray, out _))
            {
                continue;
            }

            foreach (var collider in gameObject.Colliders)
            {
                if (!collider.Intersects(ray, out var depth))
                {
                    continue;
                }

                if (closest != null && !gameObject.IsStructure && closest.IsStructure ||
                    depth < closestDepth)
                {
                    closestDepth = depth;
                    closest = gameObject;
                }
            }
        }

        return closest;
    }

    // ImGui's key handling is not relied on: the hotkey is read straight from the window's input
    // snapshot so it works regardless of ImGui focus state. Repeat KeyDown events are ignored so
    // holding Space does not retrigger.
    private bool DetectSpace(InputSnapshot snapshot)
    {
        return DetectKeyToggle(snapshot, Key.Space, ref _spaceDown);
    }

    private static bool DetectKeyToggle(InputSnapshot snapshot, Key key, ref bool wasDown)
    {
        var pressed = false;
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            if (keyEvent.Key != key)
            {
                continue;
            }

            if (keyEvent.Down && !wasDown)
            {
                pressed = true;
            }

            wasDown = keyEvent.Down;
        }

        return pressed;
    }

    public void Render(GraphicsDevice graphicsDevice)
    {
        _commandList.Begin();
        _commandList.SetFramebuffer(_window?.Swapchain.Framebuffer ?? _game.Panel.Framebuffer);
        _imgui.Render(graphicsDevice, _commandList);
        _commandList.End();
        graphicsDevice.SubmitCommands(_commandList);
    }

    public void Dispose()
    {
        _unsubscribeResize();
        _commandList.Dispose();
        _imgui.Dispose();
    }
}
