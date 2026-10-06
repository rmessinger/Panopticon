using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units.Ee2;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

public class DdfReaderTests
{
    private const string Sample =
        """
        // comment line
        UnitType HeavyInfantry1
        {
            parent = Human
            properties {
                rps = HeavyInfantry
                SizeX = 0.125 /* inline block comment */
                mass = 1
            }
            sounds { sndDeath = ["fx_death_male_01" "fx_death_male_02"] }
            abilities = [
                Attack { range = 0.2; reloadTime = 1.5; applyDamageTime = 0.5; damage = 0 }
                Move {speed = 1.7250; angSpeed = 720}
            ]
        }

        UnitModel HeavyInfantry1_01
        {
            Parent = BaseHuman
            DefaultModel = lhi1_maceman.nif
            States = [
                { StateName = Idle AnimName = sh_maceman_idle01.KF AnimVariants = [ a.KF b.KF ] AnimVariantsWeights = [0.1 0.1] }
                { StateName = Attack1 AnimName = sh_maceman_attack01.KF
                    TextKeys = [ { Name = PLAY_SOUND Data = fx_mace_swing KeyTime = 0.5 } ] }
            ]
        }
        """;

    [Fact]
    public void ReadsRecordsBlocksArraysAndNamedBlocks()
    {
        var records = DdfReader.Read(Sample, "sample.ddf");

        Assert.Equal(["UnitType", "UnitModel"], records.Select(r => r.Type));
        var unit = records[0];
        Assert.Equal("HeavyInfantry1", unit.Name);
        Assert.Equal("Human", unit.Parent);
        Assert.Equal("0.125", ((DdfBlock)unit.Body.Get("properties")!).GetText("SizeX"));

        var abilities = (DdfArray)unit.Body.Get("abilities")!;
        var attack = Assert.IsType<DdfBlock>(abilities.Items[0]);
        Assert.Equal("Attack", attack.Name);
        Assert.Equal("1.5", attack.GetText("reloadTime"));
        Assert.Equal("720", ((DdfBlock)abilities.Items[1]).GetText("angSpeed"));

        var sounds = (DdfArray)((DdfBlock)unit.Body.Get("sounds")!).Get("sndDeath")!;
        Assert.Equal("fx_death_male_02", ((DdfScalar)sounds.Items[1]).Text);
    }

    [Fact]
    public void KeysAreCaseInsensitiveAndPositionsAreKept()
    {
        var model = DdfReader.Read(Sample, "sample.ddf")[1];

        Assert.Equal("BaseHuman", model.Parent);
        var states = (DdfArray)model.Body.Get("states")!;
        var attack = (DdfBlock)states.Items[1];
        Assert.Equal("sh_maceman_attack01.KF", attack.GetText("AnimName"));
        Assert.Equal(new DdfPosition("sample.ddf", 17, 1), model.Position);
        Assert.Equal(23, attack.Position.Line);
    }

    [Theory]
    [InlineData("UnitType A { x = 1", "not closed")]
    [InlineData("UnitType A { x = [ 1 2 }", "unexpected '}'")]
    [InlineData("UnitType A { x = \"open }", "quoted string")]
    [InlineData("UnitType A { /* never closed", "block comment")]
    [InlineData("UnitType { }", "record name")]
    [InlineData("{ }", "record type")]
    [InlineData("UnitType A { = 1 }", "member key")]
    public void MalformedOrTruncatedInputFailsWithPosition(string text, string expected)
    {
        var exception = Assert.Throws<InvalidDataException>(() => DdfReader.Read(text, "bad.ddf"));

        Assert.Contains(expected, exception.Message);
        Assert.StartsWith("bad.ddf", exception.Message);
    }

    [Fact]
    public void NestingLimitIsEnforced()
    {
        var deep = "UnitType A { x = " + string.Concat(Enumerable.Repeat("[", DdfReader.MaxDepth + 1)) + string.Concat(Enumerable.Repeat("]", DdfReader.MaxDepth + 1)) + " }";

        Assert.Contains("nesting", Assert.Throws<InvalidDataException>(() => DdfReader.Read(deep, "deep.ddf")).Message);
    }

    [Fact]
    public void SizeLimitIsEnforced()
    {
        var huge = new string(' ', DdfReader.MaxFileBytes + 1);

        Assert.Contains("exceeds", Assert.Throws<InvalidDataException>(() => DdfReader.Read(huge, "huge.ddf")).Message);
    }

    [Fact]
    public void DuplicateRecordsAreKeptForTheAdapterToReport()
    {
        var records = DdfReader.Read("UnitType A { } UnitType A { x = 1 }", "dup.ddf");

        Assert.Equal(2, records.Count(r => r.Name == "A"));
    }

    [Fact]
    public void CsvTableReadsHeaderAndQuotedCells()
    {
        const string Csv =
            """"
            // UPGRADE,UNIT,TYPE,EPOCH,HP,DAMAGE
            HeavyInfantry1UpgradeEpoch1,HeavyInfantry1,"""Unit""",1,75,5
            """";

        var table = Ee2CsvTable.Read(Csv, "u.csv");

        Assert.True(table.TryGetRow("HeavyInfantry1UpgradeEpoch1", out var row, out var line));
        Assert.Equal("75", row["HP"]);
        Assert.Equal("\"Unit\"", row["TYPE"]);
        Assert.Equal(2, line);
        Assert.Throws<InvalidDataException>(() => Ee2CsvTable.Read(Csv + "\n" + Csv.Split('\n')[1], "u.csv"));
    }
}
