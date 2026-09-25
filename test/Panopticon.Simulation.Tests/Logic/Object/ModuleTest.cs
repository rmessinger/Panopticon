using System.IO;

namespace Panopticon.Tests.Logic.Object;

public abstract class ModuleTest : StatePersisterTest
{
    protected virtual byte[] ModuleData() => [V1];

    protected override MemoryStream SaveData(byte[] data, byte version = V1)
    {
        return new MemoryStream([version, .. ModuleData(), .. data]);
    }
}
