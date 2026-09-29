#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Converts legacy EE2 NiParticles effect NIFs into CPU interchange descriptors.
/// Emits source units only; the materializer (WI-C4h-04) owns engine-unit conversion.
/// See docs/roadmap/deep-dives/c4h-ee2-particle-effects.md.
/// </summary>
internal static class NifParticleEffectConverter
{
    private const float Pi = MathF.PI;

    internal static ImportedParticleEffect? Convert(NifFile file, string effectName)
    {
        var systems = new List<ImportedParticleSystem>();
        var helperNames = new List<string>();

        for (var i = 0; i < file.Blocks.Count; i++)
        {
            switch (file.Blocks[i])
            {
                case NifParticles particles:
                    systems.Add(ConvertSystem(file, effectName, i, particles));
                    break;
                case NifTriShape shape when shape.Name.Length > 0:
                    helperNames.Add(shape.Name);
                    break;
                case NifTriStrips strips when strips.Name.Length > 0:
                    helperNames.Add(strips.Name);
                    break;
            }
        }

        if (systems.Count == 0)
        {
            return null;
        }

        return new ImportedParticleEffect(effectName, systems, helperNames);
    }

    private static ImportedParticleSystem ConvertSystem(NifFile file, string effectName, int index, NifParticles particles)
    {
        if (particles.ControllerReference < 0 || file.Blocks[particles.ControllerReference] is not NifParticleSystemController controller)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticles block {index} has no NiParticleSystemController; C4h imports legacy NiParticles systems only.");
        }

        if (controller.EmitterReference >= 0 && file.Blocks[controller.EmitterReference] is NifGeometry)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticleSystemController block {particles.ControllerReference} emits from mesh block {controller.EmitterReference}; C4h does not import mesh emission.");
        }

        var name = particles.Name.Length > 0 ? particles.Name : $"Particles{index}";
        var localTransform = NifTransformConverter.ConjugateSourceToEngine(
            NifTransformConverter.CreateLocalMatrix(particles));

        // Conjugation converts the translation to feet (the C4c bone-local
        // convention); the interchange carries source units, so undo that
        // scale. The rotation part conjugates without any scale change.
        localTransform.Translation *= NifTransformConverter.MetersPerFoot;

        var emissionStart = MathF.Max(0, controller.EmitStartTime);
        var emissionStop = controller.EmitStopTime;

        if (emissionStop < emissionStart)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticleSystemController block {particles.ControllerReference} stops emitting before it starts.");
        }

        if (controller.Lifetime <= 0 || controller.Lifetime - controller.LifetimeVariation <= 0)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticleSystemController block {particles.ControllerReference} has a non-positive particle lifetime.");
        }

        if (controller.InitialSize <= 0)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticleSystemController block {particles.ControllerReference} has a non-positive initial size.");
        }

        var velocity = ConvertVelocity(controller);
        var emitter = ConvertEmitter(effectName, particles.ControllerReference, controller.EmitterDimensions);
        var modifiers = ConvertModifiers(file, effectName, controller);

        var textureName = ResolveTextureName(file, particles);
        var materialName = ResolveMaterialName(file, particles);
        var alphaBlended = IsAlphaBlended(file, particles);

        var alphaKeys = CollectAlphaKeys(file, effectName, index, particles, controller);

        return new ImportedParticleSystem(
            name,
            localTransform,
            controller.BirthRate,
            controller.UseBirthRate,
            emissionStart,
            emissionStop,
            controller.Lifetime,
            controller.LifetimeVariation,
            new Vector3(controller.InitialColor.R, controller.InitialColor.G, controller.InitialColor.B),
            controller.InitialColor.A,
            controller.InitialSize,
            velocity,
            emitter,
            modifiers,
            alphaKeys,
            textureName,
            materialName,
            alphaBlended);
    }

    private static ImportedParticleVelocity ConvertVelocity(NifParticleSystemController controller)
    {
        var speedLow = MathF.Max(0, controller.Speed - controller.SpeedVariation);
        var speedHigh = controller.Speed + controller.SpeedVariation;
        var axis = new Vector3(
            -controller.InitialNormal.Y,
            controller.InitialNormal.X,
            controller.InitialNormal.Z);

        var coversSphere = controller.Declination + controller.DeclinationVariation >= Pi &&
            controller.Declination - controller.DeclinationVariation <= 0 &&
            controller.PlanarAngleVariation >= Pi;

        if (coversSphere)
        {
            return new ImportedParticleVelocity(
                ImportedParticleVelocityKind.Spherical, axis, speedLow, speedHigh, 0);
        }

        if (axis.LengthSquared() < 1e-10f)
        {
            axis = Vector3.UnitZ;
        }

        return new ImportedParticleVelocity(
            ImportedParticleVelocityKind.Cone, axis, speedLow, speedHigh, controller.DeclinationVariation);
    }

    private static ImportedParticleEmitter ConvertEmitter(string effectName, int controllerIndex, Vector3 dimensions)
    {
        var nonZeroAxes = (dimensions.X != 0 ? 1 : 0) + (dimensions.Y != 0 ? 1 : 0) + (dimensions.Z != 0 ? 1 : 0);

        var kind = nonZeroAxes switch
        {
            0 => ImportedParticleEmitterKind.Point,
            1 => ImportedParticleEmitterKind.Line,
            _ => ImportedParticleEmitterKind.Box,
        };

        return new ImportedParticleEmitter(kind, dimensions);
    }

    private static List<ImportedParticleModifier> ConvertModifiers(NifFile file, string effectName, NifParticleSystemController controller)
    {
        var modifiers = new List<ImportedParticleModifier>();
        var visited = new HashSet<int>();

        WalkModifierChain(file, effectName, controller.EmitterModifierReference, modifiers, visited);
        WalkModifierChain(file, effectName, controller.ParticleModifierReference, modifiers, visited);

        return modifiers;
    }

    private static void WalkModifierChain(
        NifFile file,
        string effectName,
        int head,
        List<ImportedParticleModifier> modifiers,
        HashSet<int> visited)
    {
        var current = head;

        while (current >= 0)
        {
            if (!visited.Add(current))
            {
                return;
            }

            var modifier = (NifParticleModifier)file.Blocks[current];

            switch (modifier)
            {
                case NifParticleColorModifier colorModifier:
                    modifiers.Add(ConvertColorOverLife(file, effectName, current, colorModifier));
                    break;
                case NifParticleGrowFade growFade:
                    modifiers.Add(ImportedParticleModifier.CreateGrowFade(growFade.GrowTime, growFade.FadeTime));
                    break;
                case NifGravity gravity:
                    {
                        var direction = new Vector3(-gravity.Direction.Y, gravity.Direction.X, gravity.Direction.Z);

                        if (direction.LengthSquared() > 1e-10f)
                        {
                            direction = Vector3.Normalize(direction);
                        }

                        modifiers.Add(ImportedParticleModifier.CreateGravity(gravity.Force, direction, gravity.Decay));
                        break;
                    }

                default:
                    modifiers.Add(ImportedParticleModifier.CreateUnsupported(modifier.GetType().Name));
                    break;
            }

            current = modifier.NextModifierReference;
        }
    }

    private static ImportedParticleModifier ConvertColorOverLife(
        NifFile file,
        string effectName,
        int modifierIndex,
        NifParticleColorModifier colorModifier)
    {
        if (colorModifier.ColorDataReference < 0 ||
            file.Blocks[colorModifier.ColorDataReference] is not NifColorData colorData ||
            colorData.Keys is null ||
            colorData.Keys.Keys.Count == 0)
        {
            throw new NotSupportedException(
                $"Effect '{effectName}': NiParticleColorModifier block {modifierIndex} has no color keys.");
        }

        var keys = colorData.Keys.Keys
            .Select(k => new ImportedParticleColorKey(k.Time, k.Value.R, k.Value.G, k.Value.B, k.Value.A))
            .ToArray();

        return ImportedParticleModifier.CreateColorOverLife(keys);
    }

    private static string? ResolveTextureName(NifFile file, NifParticles particles)
    {
        foreach (var reference in particles.PropertyReferences)
        {
            if (file.Blocks[reference] is not NifTexturingProperty texturing)
            {
                continue;
            }

            if (!texturing.Slots.TryGetValue("Base", out var slot) || slot.SourceReference < 0)
            {
                continue;
            }

            if (file.Blocks[slot.SourceReference] is NifSourceTexture source && source.FileName.Length > 0)
            {
                return source.FileName;
            }
        }

        return null;
    }

    private static string? ResolveMaterialName(NifFile file, NifParticles particles)
    {
        foreach (var reference in particles.PropertyReferences)
        {
            if (file.Blocks[reference] is NifMaterialProperty material && material.Name.Length > 0)
            {
                return material.Name;
            }
        }

        return null;
    }

    private static bool IsAlphaBlended(NifFile file, NifParticles particles)
    {
        foreach (var reference in particles.PropertyReferences)
        {
            if (file.Blocks[reference] is NifAlphaProperty alpha && (alpha.Flags & 1) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<ImportedParticleAlphaKey> CollectAlphaKeys(
        NifFile file,
        string effectName,
        int particlesIndex,
        NifParticles particles,
        NifParticleSystemController controller)
    {
        var keys = new List<ImportedParticleAlphaKey>();
        var visitedControllers = new HashSet<int>();

        // Alpha controllers animate the NiAlphaProperty; also accept one chained
        // behind the particle controller for layouts that attach it to the system.
        foreach (var reference in particles.PropertyReferences)
        {
            if (file.Blocks[reference] is NifAlphaProperty alpha)
            {
                CollectAlphaKeysFromControllerChain(
                    file, effectName, particlesIndex, alpha.ControllerReference, keys, visitedControllers);
            }

            if (file.Blocks[reference] is NifMaterialProperty material)
            {
                RejectMaterialColorController(file, effectName, particlesIndex, material.ControllerReference);
            }
        }

        CollectAlphaKeysFromControllerChain(
            file, effectName, particlesIndex, controller.NextControllerReference, keys, visitedControllers);

        return keys.OrderBy(k => k.TimeSeconds).ThenBy(k => k.Alpha).ToList();
    }

    private static void CollectAlphaKeysFromControllerChain(
        NifFile file,
        string effectName,
        int particlesIndex,
        int head,
        List<ImportedParticleAlphaKey> keys,
        HashSet<int> visitedControllers)
    {
        var current = head;

        while (current >= 0 && visitedControllers.Add(current))
        {
            var block = file.Blocks[current];

            if (block is NifAlphaController alphaController)
            {
                if (alphaController.DataReference >= 0 &&
                    file.Blocks[alphaController.DataReference] is NifFloatData floatData &&
                    floatData.Keys is { } group)
                {
                    foreach (var key in group.Keys)
                    {
                        keys.Add(new ImportedParticleAlphaKey(
                            MathF.Max(0, key.Time - alphaController.StartTime), key.Value));
                    }
                }

                return;
            }

            if (block is NifMaterialColorController)
            {
                throw NewMaterialColorRejected(effectName, particlesIndex, current);
            }

            if (block is NifTextureTransformController)
            {
                throw new NotSupportedException(
                    $"Effect '{effectName}': NiParticles block {particlesIndex} carries a NiTextureTransformController (block {current}); C4h does not import animated particle textures.");
            }

            if (block is not NifTimeController timeController)
            {
                return;
            }

            current = timeController.NextControllerReference;
        }
    }

    private static void RejectMaterialColorController(NifFile file, string effectName, int particlesIndex, int head)
    {
        var current = head;

        while (current >= 0)
        {
            if (file.Blocks[current] is NifMaterialColorController)
            {
                throw NewMaterialColorRejected(effectName, particlesIndex, current);
            }

            if (file.Blocks[current] is not NifTimeController controller)
            {
                return;
            }

            current = controller.NextControllerReference;
        }
    }

    private static NotSupportedException NewMaterialColorRejected(string effectName, int particlesIndex, int controllerIndex)
    {
        return new NotSupportedException(
            $"Effect '{effectName}': NiParticles block {particlesIndex} carries a NiMaterialColorController (block {controllerIndex}); C4h does not import animated particle material colors.");
    }
}
