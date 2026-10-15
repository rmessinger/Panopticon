using System;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public enum HarnessScenarioCategory { Replay, ValidationGround, Capture, Proof, MassBattle, Physics }

[Flags]
public enum HarnessRunModes { None = 0, Headless = 1, Windowed = 2 }

public enum HarnessProfileInput { None, File, FileOrEmbedded }

public enum HarnessParameterKind { Bool, Int, Float, String, Path, Enum, Json }
