#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Panopticon.Audio;
using Panopticon.Content;
using Panopticon.FX;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Gui;
using Panopticon.Gui.ControlBar;
using Panopticon.Logic;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Rebinds LazyAssetReference fields and properties on reconstructed native assets
/// to resolve against the target publication AssetStore rather than the isolated in-memory
/// rebuild store.
/// </summary>
internal static class RetainedReferenceRebinder
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> TypedRebinders = new();

    public static void RebindAll(IEnumerable<BaseAsset> assets, AssetStore store)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(store);

        foreach (var asset in assets)
        {
            Rebind(asset, store);
        }
    }

    public static void Rebind(object? root, AssetStore store)
    {
        if (root is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(store);

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        TraverseAndRebind(root, store, visited);
    }

    private static void TraverseAndRebind(object? obj, AssetStore store, HashSet<object> visited)
    {
        if (obj is null)
        {
            return;
        }

        var type = obj.GetType();
        if (type.IsPrimitive || obj is string || type.IsEnum || type.IsPointer)
        {
            return;
        }

        if (obj is byte[] or sbyte[] or short[] or ushort[] or int[] or uint[]
            or long[] or ulong[] or float[] or double[] or decimal[] or bool[]
            or char[] or string[])
        {
            return;
        }

        if (obj is AssetStore or Game or IGameEngine or GameObject or IO.FileSystem)
        {
            return;
        }

        if (!visited.Add(obj))
        {
            return;
        }

        if (obj is ILazyAssetReference lazyRef)
        {
            RebindLazyReference(lazyRef, store);
            return;
        }

        if (obj is ObjectDefinition def)
        {
            RebindObjectDefinitionLocomotors(def, store);
        }

        if (obj is IEnumerable enumerable)
        {
            if (obj is IDictionary dict)
            {
                foreach (var value in dict.Values)
                {
                    TraverseAndRebind(value, store, visited);
                }

                return;
            }

            foreach (var item in enumerable)
            {
                TraverseAndRebind(item, store, visited);
            }

            return;
        }

        var ns = type.Namespace;
        if (ns is null || (!ns.StartsWith("Panopticon", StringComparison.Ordinal) && !ns.StartsWith("Veldrid", StringComparison.Ordinal)))
        {
            return;
        }

        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var fields = currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType.IsEnum)
                {
                    continue;
                }

                object? fieldValue;
                try
                {
                    fieldValue = field.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (fieldValue is not null)
                {
                    TraverseAndRebind(fieldValue, store, visited);
                }
            }

            currentType = currentType.BaseType;
        }
    }

    private static void RebindObjectDefinitionLocomotors(ObjectDefinition def, AssetStore store)
    {
        if (def.LocomotorSets is null)
        {
            return;
        }

        foreach (var set in def.LocomotorSets.Values)
        {
            if (set?.Locomotors is null)
            {
                continue;
            }

            for (var i = 0; i < set.Locomotors.Length; i++)
            {
                var locoRef = set.Locomotors[i];
                if (locoRef is not null && !string.IsNullOrEmpty(locoRef.Name))
                {
                    var locoName = locoRef.Name;
                    var rebound = new LazyAssetReference<LocomotorTemplate>(locoName, () => store.LocomotorTemplates.GetByName(locoName));
                    set.Locomotors[i] = rebound;
                    RebindTyped(locoRef, store);
                }
            }
        }
    }

    private static void RebindLazyReference(ILazyAssetReference reference, AssetStore store)
    {
        var refType = reference.GetType();
        if (!refType.IsGenericType || refType.GetGenericTypeDefinition() != typeof(LazyAssetReference<>))
        {
            return;
        }

        var assetType = refType.GetGenericArguments()[0];
        var rebindMethod = TypedRebinders.GetOrAdd(assetType, static t =>
            typeof(RetainedReferenceRebinder)
                .GetMethod(nameof(RebindTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(t));

        rebindMethod.Invoke(null, [reference, store]);
    }

    private static void RebindTyped<TAsset>(LazyAssetReference<TAsset> reference, AssetStore store)
        where TAsset : BaseAsset
    {
        var name = reference.Name;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var lookup = LookupCache<TAsset>.Lookup;
        if (lookup is null)
        {
            return;
        }

        var getValueField = typeof(LazyAssetReference<TAsset>).GetField("_getValue", BindingFlags.Instance | BindingFlags.NonPublic);
        var valueField = typeof(LazyAssetReference<TAsset>).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic);

        Func<TAsset> getter = () => lookup(store, name)!;
        getValueField?.SetValue(reference, getter);
        valueField?.SetValue(reference, null);
    }

    private static class LookupCache<TAsset>
        where TAsset : BaseAsset
    {
        public static readonly Func<AssetStore, string, TAsset?>? Lookup = ResolveLookup();

        private static Func<AssetStore, string, TAsset?>? ResolveLookup()
        {
            var targetType = typeof(TAsset);

            if (targetType == typeof(LocomotorTemplate))
            {
                return (s, n) => (s.LocomotorTemplates.GetByName(n) as TAsset);
            }

            if (targetType == typeof(SpecialPower))
            {
                return (s, n) => (s.SpecialPowers.GetByName(n) as TAsset);
            }

            if (targetType == typeof(WeaponTemplate))
            {
                return (s, n) => (s.WeaponTemplates.GetByName(n) as TAsset);
            }

            if (targetType == typeof(ArmorTemplate))
            {
                return (s, n) => (s.ArmorTemplates.GetByName(n) as TAsset);
            }

            if (targetType == typeof(UpgradeTemplate))
            {
                return (s, n) => (s.Upgrades.GetByName(n) as TAsset);
            }

            if (targetType == typeof(ObjectDefinition))
            {
                return (s, n) => (s.ObjectDefinitions.GetByName(n) as TAsset);
            }

            if (targetType == typeof(DamageFX))
            {
                return (s, n) => (s.DamageFXs.GetByName(n) as TAsset);
            }

            if (targetType == typeof(FXList))
            {
                return (s, n) => (s.FXLists.GetByName(n) as TAsset);
            }

            if (targetType == typeof(FXParticleSystemTemplate))
            {
                return (s, n) => (s.FXParticleSystemTemplates.GetByName(n) as TAsset);
            }

            if (targetType == typeof(CommandSet))
            {
                return (s, n) => (s.CommandSets.GetByName(n) as TAsset);
            }

            if (targetType == typeof(CommandButton))
            {
                return (s, n) => (s.CommandButtons.GetByName(n) as TAsset);
            }

            if (targetType == typeof(MappedImage))
            {
                return (s, n) => (s.MappedImages.GetByName(n) as TAsset);
            }

            if (targetType == typeof(Science))
            {
                return (s, n) => (s.Sciences.GetByName(n) as TAsset);
            }

            if (targetType == typeof(CrateData))
            {
                return (s, n) => (s.CrateDatas.GetByName(n) as TAsset);
            }

            if (targetType == typeof(ModifierList))
            {
                return (s, n) => (s.ModifierLists.GetByName(n) as TAsset);
            }

            if (targetType == typeof(AudioFile))
            {
                return (s, n) => (s.AudioFiles.GetByName(n) as TAsset);
            }

            if (targetType == typeof(AudioEvent))
            {
                return (s, n) => (s.AudioEvents.GetByName(n) as TAsset);
            }

            if (targetType == typeof(BaseAudioEventInfo))
            {
                return (s, n) => (s.AudioEvents.GetByName(n) as TAsset);
            }

            if (targetType == typeof(Model))
            {
                return (s, n) => (s.Models.GetByName(n) as TAsset);
            }

            if (targetType == typeof(ModelAnimation))
            {
                return (s, n) => (s.ModelAnimations.GetByName(n) as TAsset);
            }

            if (targetType == typeof(TextureAsset))
            {
                return (s, n) => (s.Textures.GetByName(n) as TAsset);
            }

            if (targetType == typeof(GuiTextureAsset))
            {
                return (s, n) => (s.GuiTextures.GetByName(n) as TAsset);
            }

            foreach (var prop in typeof(AssetStore).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = prop.PropertyType;
                if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(ScopedAssetCollection<>))
                {
                    var itemType = propType.GetGenericArguments()[0];
                    if (targetType.IsAssignableFrom(itemType))
                    {
                        var getByNameMethod = propType.GetMethod("GetByName", [typeof(string)]);
                        if (getByNameMethod != null)
                        {
                            return (s, n) =>
                            {
                                var coll = prop.GetValue(s);
                                return coll != null ? (TAsset?)getByNameMethod.Invoke(coll, [n]) : null;
                            };
                        }
                    }
                }
            }

            return null;
        }
    }
}
