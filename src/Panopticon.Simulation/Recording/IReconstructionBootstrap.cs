namespace Panopticon.Recording;

/// <summary>
/// Boots the game, scenario, and domain model a bundle was recorded with (WI-A1-22). The
/// reconstructor restores checkpoints into hosts this interface produces; the boot must reach
/// the same starting state the recorded run attached at (same install, scenario, seed, roster,
/// and domain model) or every boundary hash will diverge.
/// </summary>
public interface IReconstructionBootstrap
{
    /// <summary>
    /// Boots a fresh host at frame zero, before any frames have stepped. Called once per
    /// <see cref="RunReconstructor.Reconstruct"/> call; returned hosts are not reused.
    /// </summary>
    SimulationHost Boot();
}
