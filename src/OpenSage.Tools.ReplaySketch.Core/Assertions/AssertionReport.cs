using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenSage.Tools.ReplaySketch.Assertions;

public sealed class AssertionReport
{
    public static readonly AssertionReport Empty = new([]);

    public IReadOnlyList<AssertionResult> Results { get; }
    public bool AllPassed => Results.All(result => result.Passed);

    public AssertionReport(IReadOnlyList<AssertionResult> results)
    {
        Results = results;
    }

    public string ToJson()
    {
        var payload = new
        {
            allPassed = AllPassed,
            results = Results.Select(result => new
            {
                id = result.AssertionId,
                passed = result.Passed,
                actual = result.ActualValue,
                failFrame = result.FailFrame,
                message = result.Message,
            }),
        };

        return JsonSerializer.Serialize(payload);
    }
}
