using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Panopticon.Client;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Diagnostics.AssetViews;

[AssetView(typeof(ObjectDefinition))]
internal sealed class GameObjectView : AssetView
{
    private GameObject _gameObject;
    private Drawable _drawable;
    private readonly List<BitArray<ModelConditionFlag>> _modelConditionStates;
    private int _selectedIndex;

    private readonly RenderedView _renderedView;

    public GameObjectView(DiagnosticViewContext context, ObjectDefinition objectDefinition)
        : base(context)
    {
        _renderedView = AddDisposable(new RenderedView(context,
            createGameObjects: gameObjects =>
            {
                _gameObject = gameObjects.CreateObject(objectDefinition, null);
            }));

        _drawable = (Drawable)_gameObject.Client!;
        _modelConditionStates = _drawable.ModelConditionStates.ToList();
        _selectedIndex = 0;
    }

    public override void Draw()
    {
        ImGui.BeginChild("object states", new Vector2(200, 0), ImGuiChildFlags.Borders, 0);

        for (var i = 0; i < _modelConditionStates.Count; i++)
        {
            var modelConditionState = _modelConditionStates[i];

            if (ImGui.Selectable(modelConditionState.DisplayName, i == _selectedIndex))
            {
                _drawable.CopyModelConditionFlags(modelConditionState);
                _selectedIndex = i;
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();

        _renderedView.Draw();
    }
}
