namespace Panopticon.Geospatial.Payload.Storage;

// Per-instance seam over destructive directory operations so tests inject faults without global hooks.
internal interface IPayloadDirectoryIo
{
    void CreateDirectory(string path);

    void MoveDirectory(string sourceDirectory, string destinationDirectory);

    void DeleteDirectory(string path);
}
