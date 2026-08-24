using System;
using System.Numerics;
using ImGuiNET;
using OpenSage.Gui;
using Veldrid;

namespace OpenSage.Tools.TestHarness;

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
/// ImGui overlay for the windowed validation ground: the scenario starts paused so the starting
/// conditions can be inspected; Space (or the button) resumes, and once running, toggles active
/// pause. Renders on top of the copied game frame (same pattern as the engine's
/// DeveloperModeView). Space is owned by the overlay and filtered out of the game's input queue.
/// The status window starts hidden (F1 toggles it); a mouse-over tooltip always shows the
/// hovered object's details. Both Space and F1 are read from the raw input snapshot, so they work
/// while the status window is hidden.
/// </summary>
public sealed class ValidationGroundOverlay : IDisposable
{
    private readonly ImGuiRenderer _imgui;
    private readonly CommandList _commandList;
    private readonly GameWindow _window;
    private readonly Game _game;
    private readonly Action _unsubscribeResize;

    private bool _spaceDown;
    private bool _f1Down;
    private bool _statusWindowVisible;

    public ValidationGroundOverlayState State { get; set; } = new(false, false, 0, 0, 0);

    /// <summary>Set for one frame when the button or Space activates the current primary action
    /// (resume-from-hold while held, pause toggle afterwards). The runner consumes it.</summary>
    public bool PrimaryActionRequested { get; private set; }

    /// <summary>True when ImGui wants the mouse this frame; the runner drops mouse messages then.</summary>
    public bool WantCaptureMouse { get; private set; }

    public ValidationGroundOverlay(Game game, GameWindow window)
    {
        _game = game;
        _window = window;
        _imgui = new ImGuiRenderer(
            game.GraphicsDevice,
            window.Swapchain.Framebuffer.OutputDescription,
            window.ClientBounds.Width,
            window.ClientBounds.Height);

        void OnWindowSizeChanged(object? sender, EventArgs e)
        {
            _imgui.WindowResized(window.ClientBounds.Width, window.ClientBounds.Height);
        }

        window.ClientSizeChanged += OnWindowSizeChanged;
        _unsubscribeResize = () => window.ClientSizeChanged -= OnWindowSizeChanged;

        _commandList = game.GraphicsDevice.ResourceFactory.CreateCommandList();
    }

    public void Update(float deltaSeconds, InputSnapshot snapshot)
    {
        PrimaryActionRequested = DetectSpace(snapshot);
        if (DetectKeyToggle(snapshot, Key.F1, ref _f1Down))
        {
            _statusWindowVisible = !_statusWindowVisible;
        }

        _imgui.Update(deltaSeconds, snapshot);

        if (_statusWindowVisible)
        {
            DrawStatusWindow();
        }

        DrawObjectTooltip();

        WantCaptureMouse = _statusWindowVisible && ImGui.GetIO().WantCaptureMouse;
    }

    private void DrawStatusWindow()
    {
        var state = State;
        ImGui.SetNextWindowPos(new Vector2(16, 16), ImGuiCond.Appearing, Vector2.Zero);
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

        var mouse = ImGui.GetIO().MousePos;
        ImGui.SetNextWindowPos(new Vector2(mouse.X + 16, mouse.Y + 12), ImGuiCond.Always, Vector2.Zero);
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
    private OpenSage.Logic.Object.GameObject? FindHoveredObject()
    {
        var scene = _game.Scene3D;
        if (scene == null || ImGui.GetIO().WantCaptureMouse)
        {
            return null;
        }

        var mouse = ImGui.GetIO().MousePos;
        var ray = scene.Camera.ScreenPointToRay(new Vector2(mouse.X, mouse.Y));

        var closestDepth = float.MaxValue;
        OpenSage.Logic.Object.GameObject? closest = null;

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
        _commandList.SetFramebuffer(_window.Swapchain.Framebuffer);
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
