#nullable enable

using System.Collections.Generic;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Evaluates one genome in one seeded match (WI-A4-15). Implementations wrap the headless
/// harness; tests supply fakes so the runner stays game-independent.
/// </summary>
public interface INeuralPolicyMatchEvaluator
{
    NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed);
}
