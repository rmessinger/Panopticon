using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Tools.ReplaySketch.Assertions;

public sealed class CellResult
{
    public IReadOnlyDictionary<string, string> Key { get; }
    public AssertionReport Report { get; }

    public CellResult(IReadOnlyDictionary<string, string> key, AssertionReport report)
    {
        Key = key;
        Report = report;
    }
}

public sealed class MatrixReport
{
    public IReadOnlyList<CellResult> Cells { get; }
    public bool AllPassed => Cells.All(cell => cell.Report.AllPassed);

    public MatrixReport(IReadOnlyList<CellResult> cells)
    {
        Cells = cells;
    }

    public string ToJson()
    {
        var total = Cells.Count;
        var passed = Cells.Count(cell => cell.Report.AllPassed);
        var failed = total - passed;
        var payload = new
        {
            totalCells = total,
            passed,
            failed,
            cells = Cells.Select(cell => new
            {
                key = cell.Key,
                status = cell.Report.AllPassed ? "passed" : "failed",
                assertions = cell.Report.Results.Select(result => new
                {
                    id = result.AssertionId,
                    passed = result.Passed,
                    actual = result.ActualValue,
                    failFrame = result.FailFrame,
                    message = result.Message,
                }),
            }),
        };

        return JsonSerializer.Serialize(payload);
    }
}
