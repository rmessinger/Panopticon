using System.Collections.Generic;
using OpenSage.Data;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch;

public static class ScenarioMatrixRunner
{
    public static MatrixReport Run(ScenarioMatrix matrix, GameInstallation installation, string tempDirectory)
    {
        var cells = matrix.Expand();
        var results = new List<CellResult>(cells.Count);

        foreach (var cell in cells)
        {
            var report = HarnessRunner.RunAndAssert(cell.ConcreteScenario, installation, tempDirectory);
            results.Add(new CellResult(cell.Bindings, report));
        }

        return new MatrixReport(results);
    }
}
