using System.Numerics;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Diagnostics.AssetViews;

[AssetView(typeof(Model))]
internal sealed class ModelView : AssetView
{
    private readonly RenderedView _renderedView;
    private readonly ModelInstance _modelInstance;

    public ModelView(DiagnosticViewContext context, Model model)
        : base(context)
    {
        _modelInstance = AddDisposable(model.CreateInstance(context.Game.AssetStore.LoadContext));
        MuzzleFlashBones.HideForStandaloneRender(_modelInstance, context.Game.AssetStore.ObjectDefinitions);
        _modelInstance.Update(TimeInterval.Zero);

        var enclosingBoundingBox = GetEnclosingBoundingBox(_modelInstance);

        _renderedView = AddDisposable(new RenderedView(
            context,
            enclosingBoundingBox.GetCenter(),
            Vector3.Distance(enclosingBoundingBox.Min, enclosingBoundingBox.Max)));

        void OnBuildingRenderList(object sender, BuildingRenderListEventArgs e)
        {
            _modelInstance.SetWorldMatrix(Matrix4x4.Identity);
            _modelInstance.BuildRenderList(
                e.RenderList,
                e.Camera,
                true,
                new MeshShaderResources.RenderItemConstantsPS
                {
                    HouseColor = Vector3.One,
                    Opacity = 1.0f,
                    TintColor = Vector3.One
                });
        }

        _renderedView.RenderPipeline.BuildingRenderList += OnBuildingRenderList;
        AddDisposeAction(() => _renderedView.RenderPipeline.BuildingRenderList -= OnBuildingRenderList);
    }

    private static AxisAlignedBoundingBox GetEnclosingBoundingBox(ModelInstance modelInstance)
    {
        var boundingBox = default(AxisAlignedBoundingBox);

        var first = true;
        foreach (var subObject in modelInstance.Model.SubObjects)
        {
            var transformedBoundingBox = AxisAlignedBoundingBox.Transform(subObject.RenderObject.BoundingBox,
                modelInstance.ModelBoneInstances[subObject.Bone.Index].Matrix);

            if (first)
            {
                boundingBox = transformedBoundingBox;
                first = false;
            }
            else
            {
                boundingBox = AxisAlignedBoundingBox.CreateMerged(
                    boundingBox,
                    transformedBoundingBox);
            }
        }

        return boundingBox;
    }

    public override void Draw()
    {
        _renderedView.Draw();
    }
}
