using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Mods.BuiltIn;
using Panopticon.Rendering;
using Panopticon.Tests.Content;
using Panopticon.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Data.Ini;

public class IniFileTests
{
    private static readonly Encoding LocaleSpecificEncoding;

    static IniFileTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        LocaleSpecificEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
    }

    private readonly ITestOutputHelper _output;

    public IniFileTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact, Trait("Category", "Interactive")]
    public void CanReadIniFiles()
    {
        var gameDefinitions = new[]
        {
            GameDefinition.FromGame(SageGame.CncGenerals),
            GameDefinition.FromGame(SageGame.CncGeneralsZeroHour),
            GameDefinition.FromGame(SageGame.Bfme),
            GameDefinition.FromGame(SageGame.Bfme2),
            GameDefinition.FromGame(SageGame.Bfme2Rotwk),
        };

        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);

        using var standardGraphicsResources = new StandardGraphicsResources(graphicsDevice, GraphicsSettings.Legacy());
        using var shaderSetStore = new ShaderSetStore(graphicsDevice, RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Legacy()));
        using var shaderResources = new ShaderResourceManager(graphicsDevice, standardGraphicsResources, shaderSetStore);
        var graphicsLoadContext = new GraphicsLoadContext(graphicsDevice, standardGraphicsResources, shaderResources, shaderSetStore);

        foreach (var gameDefinition in gameDefinitions)
        {
            foreach (var installation in InstallationLocators.FindAllInstallations(gameDefinition))
            {
                using var fileSystem = installation.CreateFileSystem();

                var assetStore = new AssetStore(
                    gameDefinition.Game,
                    fileSystem,
                    LanguageUtility.ReadCurrentLanguage(gameDefinition, fileSystem),
                    graphicsDevice,
                    standardGraphicsResources,
                    shaderResources,
                    shaderSetStore,
                    gameDefinition.CreateAssetLoadStrategy());

                assetStore.PushScope();

                var dataContext = new IniDataContext();

                void LoadIniFile(FileSystemEntry entry)
                {
                    var parser = new IniParser(
                        entry,
                        assetStore,
                        gameDefinition.Game,
                        dataContext,
                        LocaleSpecificEncoding);

                    parser.ParseFile();
                }

                switch (gameDefinition.Game)
                {
                    case SageGame.Bfme:
                    case SageGame.Bfme2:
                    case SageGame.Bfme2Rotwk:
                        LoadIniFile(fileSystem.GetFile(@"Data\INI\GameData.ini")!);
                        break;
                }

                foreach (var file in fileSystem.GetFilesInDirectory("", $"*.ini", SearchOption.AllDirectories))
                {
                    var filename = Path.GetFileName(file.FilePath).ToLowerInvariant();

                    switch (filename)
                    {
                        case "webpages.ini": // Don't care about this

                        case "buttonsets.ini": // Doesn't seem to be used?
                        case "scripts.ini": // Only needed by World Builder?
                        case "commandmapdebug.ini": // Only applies to DEBUG and INTERNAL builds
                        case "fxparticlesystemcustom.ini": // Don't know if this is used, it uses Emitter property not used elsewhere
                        case "lightpoints.ini": // Don't know if this is used.

                        //added in BFME and subsequent games
                        case "optionregistry.ini": // Don't know if this is used
                        case "localization.ini": // Don't know if we need this
                            continue;

                        case "credits.ini":
                            if (gameDefinition.Game == SageGame.Bfme2Rotwk) //corrupted in rotwk (start of the block is commented out)
                            {
                                continue;
                            }

                            break;

                        //mods specific

                        //edain mod
                        case "einstellungen.ini":
                        case "einstellungendeakt.ini":
                        case "einstellungenedain.ini":
                        case "news.ini":
                            continue;

                        //unofficial patch 2.02
                        case "desktop.ini": //got into a big file somehow
                        case "2.01.ini":
                        case "disable timer.ini":
                        case "enable timer.ini":
                        case "old music.ini":
                            continue;
                        default:
                            if (filename.StartsWith("2.02")) { continue; }
                            break;
                    }

                    _output.WriteLine($"Reading file {file.FilePath}.");

                    LoadIniFile(file);
                }
            }
        }
    }

    private static (AssetStore Store, IniParser Parser) CreateParser(string iniText, SageGame game = SageGame.CncGenerals, bool advanceLine = true)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\Test.ini", iniText);

        var assetStore = new AssetStore(game, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();
        var parser = new IniParser(fileSystem.GetFile(@"Data\INI\Test.ini")!, assetStore, game, new IniDataContext(), Encoding.UTF8);
        if (advanceLine)
        {
            parser.GoToNextLine();
        }

        return (assetStore, parser);
    }

    [Theory]
    [InlineData(SageGame.CncGenerals, 30.0f, 0.3048f)]
    [InlineData(SageGame.CncGeneralsZeroHour, 30.0f, 0.3048f)]
    [InlineData(SageGame.Bfme, 30.0f, 1.8288f)]
    [InlineData(SageGame.Bfme2, 30.0f, 1.8288f)]
    [InlineData(SageGame.Bfme2Rotwk, 30.0f, 1.8288f)]
    public void ParseVelocityToLogicFrames_ScalesToMetersPerLogicFrame(SageGame game, float speedFeetPerSec, float expectedMetersPerFrame)
    {
        var (_, parser) = CreateParser(speedFeetPerSec.ToString(CultureInfo.InvariantCulture), game);
        var velocity = parser.ParseVelocityToLogicFrames();
        Assert.Equal(expectedMetersPerFrame, velocity, precision: 5);
    }

    [Theory]
    [InlineData(SageGame.CncGenerals, 900.0f, 0.3048f)]
    [InlineData(SageGame.Bfme2, 900.0f, 10.9728f)]
    public void ParseAccelerationToLogicFrames_ScalesToMetersPerLogicFrameSquared(SageGame game, float accelFeetPerSecSq, float expectedMetersPerFrameSq)
    {
        var (_, parser) = CreateParser(accelFeetPerSecSq.ToString(CultureInfo.InvariantCulture), game);
        var accel = parser.ParseAccelerationToLogicFrames();
        Assert.Equal(expectedMetersPerFrameSq, accel, precision: 4);
    }

    [Fact]
    public void ParseAngularVelocityToLogicFrames_UnchangedInUnits()
    {
        var (_, parser) = CreateParser("180.0", SageGame.CncGenerals);
        var angVel = parser.ParseAngularVelocityToLogicFrames();
        var expected = 180.0f * MathUtility.DegreesToRadiansRatio / 30.0f;
        Assert.Equal(expected, angVel, precision: 5);
    }

    [Fact]
    public void ParseDistanceFloat_ScalesByMetersPerSourceUnit()
    {
        var (_, parser) = CreateParser("10.0", SageGame.CncGenerals);
        var distance = parser.ParseDistanceFloat();
        Assert.Equal(3.048f, distance, precision: 5);
    }

    [Fact]
    public void ParseDistanceVector2_And_Vector3_ScaleComponents()
    {
        var (_, parser2) = CreateParser("X:10.0 Y:20.0", SageGame.CncGenerals);
        var v2 = parser2.ParseDistanceVector2();
        Assert.Equal(3.048f, v2.X, precision: 4);
        Assert.Equal(6.096f, v2.Y, precision: 4);

        var (_, parser3) = CreateParser("X:10.0 Y:20.0 Z:30.0", SageGame.CncGenerals);
        var v3 = parser3.ParseDistanceVector3();
        Assert.Equal(3.048f, v3.X, precision: 4);
        Assert.Equal(6.096f, v3.Y, precision: 4);
        Assert.Equal(9.144f, v3.Z, precision: 4);
    }

    [Fact]
    public void ParseDistanceFloatRange_And_RandomVariable_ScaleExtents()
    {
        var (_, parserRange) = CreateParser("10.0 20.0", SageGame.CncGenerals);
        var range = parserRange.ParseDistanceFloatRange();
        Assert.Equal(3.048f, range.Low, precision: 4);
        Assert.Equal(6.096f, range.High, precision: 4);

        var (_, parserRv) = CreateParser("10.0 20.0 UNIFORM", SageGame.CncGenerals);
        var rv = parserRv.ParseDistanceRandomVariable();
        Assert.Equal(3.048f, rv.Low, precision: 4);
        Assert.Equal(6.096f, rv.High, precision: 4);
    }

    [Fact]
    public void GameData_GravityAndCellSize_ConvertAtParseBoundary()
    {
        const string ini =
            """
            GameData
              Gravity = -900.0
              PartitionCellSize = 10.0
              TerrainResourceCellSize = 20.0
              DefaultStructureRubbleHeight = 15.0
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.CncGenerals, advanceLine: false);
        parser.ParseFile();
        var gameData = store.GameData.Current;

        Assert.Equal(-0.3048f, gameData.Gravity, precision: 4);
        Assert.Equal(3.048f, gameData.PartitionCellSize, precision: 4);
        Assert.Equal(6.096f, gameData.TerrainResourceCellSize, precision: 4);
        Assert.Equal(4.572f, gameData.DefaultStructureRubbleHeight, precision: 4);
    }

    [Fact]
    public void AQ1_ProvisionalPolicy_AllSupportedGamesDeclared_NoDefaultFallthrough()
    {
        SageGame[] supported = [SageGame.CncGenerals, SageGame.CncGeneralsZeroHour, SageGame.Bfme, SageGame.Bfme2, SageGame.Bfme2Rotwk];
        foreach (var game in supported)
        {
            var (_, parser) = CreateParser("100.0", game);
            var distance = parser.ParseDistanceFloat();
            Assert.Equal(30.48f, distance, precision: 4);
        }

        SageGame[] unsupported = [SageGame.Cnc3, SageGame.Cnc3KanesWrath, SageGame.Ra3, SageGame.Ra3Uprising, SageGame.Cnc4];
        foreach (var game in unsupported)
        {
            var (_, parser) = CreateParser("100.0", game);
            Assert.Throws<NotSupportedException>(() => parser.ParseDistanceFloat());
        }
    }

    [Fact]
    public void AQ1_NoDoubleScale_OnBfmeRows()
    {
        const string ini =
            """
            Object TestBfmeObject
              VisionRange = 100.0
              Geometry = CYLINDER
              GeometryMajorRadius = 10.0
              GeometryHeight = 20.0
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.Bfme2, advanceLine: false);
        parser.ParseFile();
        var def = store.ObjectDefinitions.GetByName("TestBfmeObject");
        Assert.NotNull(def);

        Assert.Equal(30.48f, def.VisionRange, precision: 4);
        Assert.Equal(3.048f, def.Geometry.MajorRadius, precision: 4);
        Assert.Equal(6.096f, def.Geometry.Shapes[0].Height, precision: 4);

        var policy = new UnitConversionPolicy(SageGame.Bfme2);
        var metricVision = Quantity.FromMeters(def.VisionRange);
        var distance = policy.ToDistance(metricVision);
        Assert.Equal(30.48, distance, precision: 4);
        Assert.NotEqual(30.48 * 0.3048, distance);
    }

    [Fact]
    public void NonLengthQuantities_RemainUnchanged()
    {
        const string ini =
            """
            Object TestObject
              BuildCost = 500
              BuildTime = 10.0
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.CncGenerals, advanceLine: false);
        parser.ParseFile();
        var def = store.ObjectDefinitions.GetByName("TestObject");
        Assert.NotNull(def);

        Assert.Equal(500, def.BuildCost);
        Assert.Equal(300u, def.BuildTime.Value);
    }

    [Fact]
    public void GameData_CameraHeights_ConvertAtParseBoundary()
    {
        const string ini =
            """
            GameData
              CameraHeight = 250.0
              MaxCameraHeight = 500.0
              MinCameraHeight = 50.0
              DefaultCameraMinHeight = 60.0
              DefaultCameraMaxHeight = 600.0
              CameraLockHeightDelta = 10.0
              CameraTerrainSampleRadiusForHeight = 5.0
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.CncGenerals, advanceLine: false);
        parser.ParseFile();
        var gameData = store.GameData.Current;

        // 250 * 0.3048 = 76.2 m
        Assert.Equal(76.2f, gameData.CameraHeight, precision: 4);
        // 500 * 0.3048 = 152.4 m
        Assert.Equal(152.4f, gameData.MaxCameraHeight, precision: 4);
        // 50 * 0.3048 = 15.24 m
        Assert.Equal(15.24f, gameData.MinCameraHeight, precision: 4);
        // 60 * 0.3048 = 18.288 m
        Assert.Equal(18.288f, gameData.DefaultCameraMinHeight, precision: 4);
        // 600 * 0.3048 = 182.88 m
        Assert.Equal(182.88f, gameData.DefaultCameraMaxHeight, precision: 4);
        // 10 * 0.3048 = 3.048 m
        Assert.Equal(3.048f, gameData.CameraLockHeightDelta, precision: 4);
        // 5 * 0.3048 = 1.524 m
        Assert.Equal(1.524f, gameData.CameraTerrainSampleRadiusForHeight, precision: 4);
    }

    [Fact]
    public void AudioSettings_PresentationDistances_ConvertAtParseBoundary()
    {
        const string ini =
            """
            AudioSettings
              MicrophoneDesiredHeightAboveTerrain = 100.0
              MicrophoneMinDistanceToCamera = 50.0
              MicrophoneMaxDistanceToCamera = 500.0
              ZoomMinDistance = 10.0
              ZoomMaxDistance = 100.0
              LivingWorldMicrophoneMaxDistanceToCamera = 200.0
              LivingWorldZoomMaxDistance = 300.0
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.CncGenerals, advanceLine: false);
        parser.ParseFile();
        var audio = store.AudioSettings.Current;

        Assert.Equal(30.48f, audio.MicrophoneDesiredHeightAboveTerrain, precision: 4);
        Assert.Equal(15.24f, audio.MicrophoneMinDistanceToCamera, precision: 4);
        Assert.Equal(152.4f, audio.MicrophoneMaxDistanceToCamera, precision: 4);
        Assert.Equal(3.048f, audio.ZoomMinDistance, precision: 4);
        Assert.Equal(30.48f, audio.ZoomMaxDistance, precision: 4);
        Assert.Equal(60.96f, audio.LivingWorldMicrophoneMaxDistanceToCamera, precision: 4);
        Assert.Equal(91.44f, audio.LivingWorldZoomMaxDistance, precision: 4);
    }

    [Fact]
    public void Draw_PresentationLengths_ConvertAtParseBoundary()
    {
        const string ini =
            """
            Object TestDrawObject
              Draw = W3DLaserDraw ModuleTag_Laser
                InnerBeamWidth = 10.0
                OuterBeamWidth = 20.0
                ArcHeight = 30.0
                FanWidth = 40.0
              End
              Draw = W3DStreakDraw ModuleTag_Streak
                Width = 5.0
              End
              Draw = W3DProjectileStreamDraw ModuleTag_Stream
                Width = 2.0
              End
            End
            """;

        var (store, parser) = CreateParser(ini, SageGame.CncGenerals, advanceLine: false);
        parser.ParseFile();
        var def = store.ObjectDefinitions.GetByName("TestDrawObject");
        Assert.NotNull(def);

        var laser = Assert.IsType<W3dLaserDrawModuleData>(def.Draws["ModuleTag_Laser"].Data);
        Assert.Equal(3.048f, laser.InnerBeamWidth, precision: 4);
        Assert.Equal(6.096f, laser.OuterBeamWidth, precision: 4);
        Assert.Equal(9.144f, laser.ArcHeight, precision: 4);
        Assert.Equal(12.192f, laser.FanWidth, precision: 4);

        var streak = Assert.IsType<W3dStreakDrawModuleData>(def.Draws["ModuleTag_Streak"].Data);
        Assert.Equal(1.524f, streak.Width, precision: 4);

        var stream = Assert.IsType<W3dProjectileStreamDrawModuleData>(def.Draws["ModuleTag_Stream"].Data);
        Assert.Equal(0.6096f, stream.Width, precision: 4);
    }
}
