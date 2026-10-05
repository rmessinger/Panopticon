using System;
using System.Collections.Generic;
namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// Structural validation of a <see cref="CaptureProfile"/>: IDs are unique, every reference
/// resolves, camera fields are coherent, and frames are in range. Checks that need the asset
/// store or a live scene (definitions exist, builders have builder AI, actors have physics)
/// are the runner's job; this catches everything decidable from the profile alone.
/// </summary>
public static class CaptureProfileValidator
{
    /// <summary>Returns the list of problems; empty means the profile is structurally valid.</summary>
    public static IReadOnlyList<string> Validate(CaptureProfile profile)
    {
        var errors = new List<string>();
        var actorIds = new HashSet<string>();
        var playerIds = new HashSet<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Scenario name must not be empty.");
        }

        if (profile.DurationFrames <= 0)
        {
            errors.Add($"DurationFrames must be positive (got {profile.DurationFrames}).");
        }

        if (profile.SettleFrames < 0)
        {
            errors.Add($"SettleFrames must not be negative (got {profile.SettleFrames}).");
        }

        if (profile.CaptureStride < 1)
        {
            errors.Add($"CaptureStride must be at least 1 (got {profile.CaptureStride}).");
        }

        foreach (var player in profile.Players)
        {
            if (string.IsNullOrWhiteSpace(player.Id))
            {
                errors.Add("Every player slot needs a non-empty Id.");
                continue;
            }

            if (player.Id == CaptureProfile.CivilianOwnerId)
            {
                errors.Add($"Player slot Id '{CaptureProfile.CivilianOwnerId}' is reserved for the civilian owner.");
                continue;
            }

            if (!playerIds.Add(player.Id))
            {
                errors.Add($"Player slot Id '{player.Id}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(player.Side))
            {
                errors.Add($"Player slot '{player.Id}' needs a Side (e.g. FactionAmerica).");
            }
        }

        var importedSources = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var import in profile.ContentImports)
        {
            if (import.Source != "ee2")
            {
                errors.Add($"Content import source '{import.Source}' is not supported; only 'ee2' is.");
            }

            if (import.UnitTypes.Count == 0 || import.DdfPaths.Count == 0)
            {
                errors.Add($"Content import '{import.Source}' needs UnitTypes and DdfPaths.");
            }

            if (!(import.GameUnitsPerSourceDistance > 0) || double.IsInfinity(import.GameUnitsPerSourceDistance))
            {
                errors.Add($"Content import '{import.Source}' needs a finite, positive GameUnitsPerSourceDistance.");
            }

            importedSources.Add(import.Source);
        }

        foreach (var actor in profile.Actors)
        {
            var separator = actor.Definition.IndexOf(':');
            if (separator > 0 && !importedSources.Contains(actor.Definition[..separator]))
            {
                errors.Add($"Actor '{actor.Id}' references source '{actor.Definition[..separator]}', which no ContentImports entry publishes.");
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
            {
                errors.Add("Every actor needs a non-empty Id.");
                continue;
            }

            if (!actorIds.Add(actor.Id))
            {
                errors.Add($"Actor Id '{actor.Id}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(actor.Definition))
            {
                errors.Add($"Actor '{actor.Id}' needs a Definition.");
            }

            if (actor.Owner != CaptureProfile.CivilianOwnerId && !playerIds.Contains(actor.Owner))
            {
                errors.Add(
                    $"Actor '{actor.Id}' owner '{actor.Owner}' is neither '{CaptureProfile.CivilianOwnerId}' nor a declared player slot.");
            }
        }

        var resultIds = new HashSet<string>();
        foreach (var command in profile.Commands)
        {
            if (command.Frame < 0 || command.Frame >= profile.DurationFrames)
            {
                errors.Add(
                    $"Command '{command.Describe()}' frame {command.Frame} is outside 0..{profile.DurationFrames - 1}.");
            }

            switch (command)
            {
                case CaptureAttackCommand attack:
                    if (!actorIds.Contains(attack.SourceActorId))
                    {
                        errors.Add($"Attack command references unknown source actor '{attack.SourceActorId}'.");
                    }

                    if (!actorIds.Contains(attack.TargetActorId))
                    {
                        errors.Add($"Attack command references unknown target actor '{attack.TargetActorId}'.");
                    }

                    if (attack.SourceActorId == attack.TargetActorId)
                    {
                        errors.Add($"Attack command source and target are both '{attack.SourceActorId}'.");
                    }

                    break;

                case CaptureConstructCommand construct:
                    if (string.IsNullOrWhiteSpace(construct.BuildingDefinition))
                    {
                        errors.Add($"Construct command '{construct.Describe()}' needs a BuildingDefinition.");
                    }

                    if (!actorIds.Contains(construct.BuilderActorId))
                    {
                        errors.Add($"Construct command references unknown builder actor '{construct.BuilderActorId}'.");
                    }

                    if (string.IsNullOrWhiteSpace(construct.ResultActorId))
                    {
                        errors.Add($"Construct command '{construct.Describe()}' needs a ResultActorId.");
                    }
                    else if (actorIds.Contains(construct.ResultActorId) || !resultIds.Add(construct.ResultActorId))
                    {
                        errors.Add(
                            $"Construct result Id '{construct.ResultActorId}' collides with an existing or duplicate actor Id.");
                    }

                    break;

                case CaptureApplyImpulseCommand impulse:
                    if (!actorIds.Contains(impulse.ActorId))
                    {
                        errors.Add($"ApplyImpulse command references unknown actor '{impulse.ActorId}'.");
                    }

                    break;

                case CaptureGrantUpgradeCommand grant:
                    if (!actorIds.Contains(grant.ActorId))
                    {
                        errors.Add($"GrantUpgrade command references unknown actor '{grant.ActorId}'.");
                    }

                    if (string.IsNullOrWhiteSpace(grant.UpgradeName))
                    {
                        errors.Add($"GrantUpgrade command '{grant.Describe()}' needs an UpgradeName.");
                    }

                    break;

                case CaptureMoveCommand move:
                    if (!actorIds.Contains(move.ActorId))
                    {
                        errors.Add($"Move command references unknown actor '{move.ActorId}'.");
                    }

                    break;
            }
        }

        foreach (var expectation in profile.Expectations)
        {
            var referenced = expectation switch
            {
                CaptureActorDamagedExpectation damaged => damaged.ActorId,
                CaptureActorDestroyedExpectation destroyed => destroyed.ActorId,
                CaptureConstructionProgressedExpectation progressed => progressed.ActorId,
                CaptureConstructionCompletedExpectation completed => completed.ActorId,
                CaptureMinDisplacementExpectation displacement => displacement.ActorId,
                _ => null,
            };

            if (referenced != null && !actorIds.Contains(referenced) && !resultIds.Contains(referenced))
            {
                errors.Add(
                    $"Expectation '{expectation.Describe()}' references unknown or not-yet-bound actor '{referenced}'.");
            }

            if (expectation is CaptureMinDisplacementExpectation minMove && minMove.MinDisplacement <= 0f)
            {
                errors.Add($"Expectation '{expectation.Describe()}' needs a positive MinDisplacement.");
            }

            if (expectation is CaptureProjectileOrientationExpectation orientation)
            {
                if (orientation.MinimumAlignmentDot is < -1f or > 1f)
                {
                    errors.Add(
                        $"Expectation '{expectation.Describe()}' needs MinimumAlignmentDot between -1 and 1.");
                }

                if (orientation.MinimumSamples <= 0)
                {
                    errors.Add(
                        $"Expectation '{expectation.Describe()}' needs a positive MinimumSamples.");
                }
            }
        }

        if (profile.StopWhenActorDestroyed != null
            && !actorIds.Contains(profile.StopWhenActorDestroyed)
            && !resultIds.Contains(profile.StopWhenActorDestroyed))
        {
            errors.Add(
                $"StopWhenActorDestroyed '{profile.StopWhenActorDestroyed}' is neither a declared nor a construct-result actor Id.");
        }

        ValidateCamera(profile, actorIds, errors);

        return errors;
    }

    private static void ValidateCamera(CaptureProfile profile, HashSet<string> actorIds, List<string> errors)
    {
        var camera = profile.Camera;

        switch (camera.Mode)
        {
            case CaptureCameraMode.Explicit:
                if (camera.LookAt == null)
                {
                    errors.Add("Explicit camera needs a LookAt point.");
                }

                if (camera.Height <= 0f)
                {
                    errors.Add($"Explicit camera needs a positive Height (got {camera.Height}).");
                }

                break;

            case CaptureCameraMode.AutoFit:
            case CaptureCameraMode.Follow:
                if (camera.DesiredSpanFraction <= 0f || camera.DesiredSpanFraction > 1f)
                {
                    errors.Add(
                        $"AutoFit camera DesiredSpanFraction must be in (0, 1] (got {camera.DesiredSpanFraction}).");
                }

                if (camera.MinCameraHeight > camera.MaxCameraHeight)
                {
                    errors.Add(
                        $"AutoFit camera MinCameraHeight {camera.MinCameraHeight} exceeds MaxCameraHeight {camera.MaxCameraHeight}.");
                }

                foreach (var subject in camera.SubjectActorIds)
                {
                    if (!actorIds.Contains(subject))
                    {
                        errors.Add($"AutoFit camera subject '{subject}' is not a declared actor.");
                    }
                }

                if (camera.Mode == CaptureCameraMode.Follow)
                {
                    var hasFollowActor = !string.IsNullOrWhiteSpace(camera.FollowActorId);
                    var hasProjectileSource = !string.IsNullOrWhiteSpace(camera.FollowProjectileSourceActorId);

                    if (hasFollowActor && hasProjectileSource)
                    {
                        errors.Add(
                            "Follow camera cannot set both FollowActorId and FollowProjectileSourceActorId.");
                    }

                    if (hasFollowActor && !actorIds.Contains(camera.FollowActorId))
                    {
                        errors.Add($"Follow camera actor '{camera.FollowActorId}' is not a declared actor.");
                    }

                    if (hasProjectileSource && !actorIds.Contains(camera.FollowProjectileSourceActorId))
                    {
                        errors.Add(
                            $"Follow camera projectile source '{camera.FollowProjectileSourceActorId}' is not a declared actor.");
                    }

                    if (camera.FollowFirstProjectileOnly && !hasProjectileSource)
                    {
                        errors.Add(
                            "FollowFirstProjectileOnly needs a FollowProjectileSourceActorId.");
                    }

                    if (!hasFollowActor && !hasProjectileSource && camera.SubjectActorIds.Count == 0)
                    {
                        errors.Add(
                            "Follow camera needs a FollowActorId, a FollowProjectileSourceActorId, or at least one SubjectActorIds entry.");
                    }
                }

                break;

            default:
                errors.Add($"Unknown camera mode {camera.Mode}.");
                break;
        }
    }
}
