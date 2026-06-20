using OpenSage.Logic.Object;

namespace OpenSage.Tools.ReplaySketch.Model;

public interface IObjectReferenceResolver
{
    ObjectId ResolveHandle(string commandId);
    ObjectId ResolveSelector(QuerySelectorCommandArgument selector);
}
