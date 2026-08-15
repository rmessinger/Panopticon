using System;
using System.Numerics;
using ImGuiNET;
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
/// </summary>
public sealed class ValidationGroundOverlay : IDisposable
{
    private readonly ImGuiRenderer _imgui;
    private readonly CommandList _commandList;
    private readonly GameWindow _window;
    private readonly Action _unsubscribeResize;

    private bool _spaceDown;

    public ValidationGroundOverlayState State { get; set; } = new(false, false, 0, 0, 0);

    /// <summary>Set for one frame when the button or Space activates the current primary action
    /// (resume-from-hold while held, pause toggle afterwards). The runner consumes it.</summary>
    public bool PrimaryActionRequested { get; private set; }

    /// <summary>True when ImGui wants the mouse this frame; the runner drops mouse messages then.</summary>
    public bool WantCaptureMouse { get; private set; }

    public ValidationGroundOverlay(Game game, GameWindow window)
    {
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
        WantCaptureMouse = false;

        _imgui.Update(deltaSeconds, snapshot);

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

        ImGui.End();

        WantCaptureMouse = ImGui.GetIO().WantCaptureMouse;
    }

    // ImGui's key handling is not relied on: the hotkey is read straight from the window's input
    // snapshot so it works regardless of ImGui focus state. Repeat KeyDown events are ignored so
    // holding Space does not retrigger.
    private bool DetectSpace(InputSnapshot snapshot)
    {
        var pressed = false;
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            if (keyEvent.Key != Key.Space)
            {
                continue;
            }

            if (keyEvent.Down && !_spaceDown)
            {
                pressed = true;
            }
            _spaceDown = keyEvent.Down;
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
