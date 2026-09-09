#nullable enable

using OpenSage;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One completed evaluation: the run output plus which player ran the genome.</summary>
public sealed record NeuralMatchEvaluation(RunOutput Output, string GenomePlayerName);
