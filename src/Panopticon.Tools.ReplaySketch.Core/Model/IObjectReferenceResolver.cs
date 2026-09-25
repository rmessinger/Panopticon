using Panopticon.Logic.Object;

namespace Panopticon.Tools.ReplaySketch.Model;

public interface IObjectReferenceResolver
{
    ObjectId ResolveHandle(string commandId);
    ObjectId ResolveSelector(QuerySelectorCommandArgument selector);
}
