using System.Collections.Generic;
using Panopticon.Data;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch;

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
