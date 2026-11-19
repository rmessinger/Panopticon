using System.Collections.Generic;

namespace Panopticon.Geospatial.Manifest;

/// <summary>
/// One opt-in bridge rule: straight two-point line features of one source emit an engine
/// bridge between the line endpoints as an adjacent BridgeStart/BridgeEnd placed-object
/// pair. Placement is visual only; the engine traversal layers stay stubbed, so bridges
/// carry no passability or pathfinding.
/// </summary>
/// <param name="SourceId">Exact source id whose line features this rule bridges.</param>
/// <param name="BridgeTemplate">
/// Engine bridge template name (a <c>Bridge</c> block of the installed game's Roads.ini);
/// must be one of <see cref="VerifiedTemplateNames"/>.
/// </param>
/// <param name="FeatureAttribute">Optional attribute a feature must carry to match.</param>
/// <param name="FeatureValue">
/// Optional exact attribute value a feature must carry to match; requires
/// <paramref name="FeatureAttribute"/>.
/// </param>
// The parameter default repeats DefaultBridgeTemplate because record primary-constructor
// defaults cannot reference the type's own members; the constant keeps one name for tests
// and the validator.
public sealed record BridgeRule(
    string SourceId,
    string BridgeTemplate = "Concrete",
    string? FeatureAttribute = null,
    string? FeatureValue = null)
{
    /// <summary>
    /// Template used when a manifest bridge rule omits <c>bridgeTemplate</c>. Concrete
    /// (model CBBridgeSt) is the default because its model is present in the installed
    /// W3D.big with the BRIDGE_LEFT/BRIDGE_SPAN/BRIDGE_RIGHT sub-objects the renderer
    /// requires; the ConcreteOneLane model CBBridgSm is absent from that archive.
    /// </summary>
    public const string DefaultBridgeTemplate = "Concrete";

    /// <summary>
    /// Bridge template names read from the installed Generals INI.big Data/INI/Roads.ini;
    /// the validator rejects every other name.
    /// </summary>
    public static readonly IReadOnlyList<string> VerifiedTemplateNames =
    [
        "AsianFloodBridge",
        "Concrete",
        "ConcreteFourLane",
        "ConcreteOneLane",
        "ConcreteTwoLane",
        "ConcreteWide",
        "EuropeanBridge",
        "EuropeanBridgeAsphalt",
        "EuropeanBridgeFourLane",
        "EuropeanBridgeFourLaneWIDE",
        "EuropeanBridgeSNOW",
        "EuropeanBridgeTwoLane",
        "EuropeanBridgeWide",
        "EuropeanBridgeWideSNOW",
        "EuropeanLandmarkBridge",
        "Industrial",
        "IronSectionalDoublewide",
        "IronSectionalDoublewide2",
        "KandaharBridge",
        "MonumentTrainBridge",
        "SpecialEffectsTrainCrashObject",
        "Tampico",
        "TsingMaLandmarkBridge",
        "WoodenSectional",
    ];
}
