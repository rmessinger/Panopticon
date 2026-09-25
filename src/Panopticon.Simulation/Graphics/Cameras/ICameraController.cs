using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Graphics.Cameras;

public interface IEditorCameraController
{
    float Zoom { get; set; }

    Vector3 Position { get; set; }

    void SetPitch(float pitch);

    void SetLookDirection(Vector3 lookDirection);

    void UpdateCamera(Camera camera, in EditorCameraInputState inputState, in TimeInterval gameTime);

    void GoToObject(GameObject gameObject);

}
