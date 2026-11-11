using System.Text.Json;
using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CheckpointValueCodecTests
{
    [Fact]
    public void ExactIntegerWrappersUseStringsAndRejectNumericAlternatives()
    {
        var codec = new CheckpointValueCodec();
        codec.Scalar<ShipId>("Value");
        JsonElement encoded = codec.Encode(new ShipId(ulong.MaxValue));
        Assert.Equal("18446744073709551615", encoded.GetString());
        Assert.Equal(new ShipId(ulong.MaxValue), codec.Decode<ShipId>(encoded));
        using JsonDocument numeric = JsonDocument.Parse("18446744073709551615");
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<ShipId>(numeric.RootElement));
    }

    [Fact]
    public void IntegralPrimitivesRetainTheirFullRange()
    {
        var codec = new CheckpointValueCodec();
        Assert.Equal(long.MinValue, codec.Decode<long>(codec.Encode(long.MinValue)));
        Assert.Equal(UInt128.MaxValue, codec.Decode<UInt128>(codec.Encode(UInt128.MaxValue)));
        BigInteger huge = BigInteger.One << 1_024;
        Assert.Equal(huge, codec.Decode<BigInteger>(codec.Encode(huge)));
    }

    [Theory]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("-0")]
    [InlineData("1e2")]
    [InlineData(" 1")]
    public void IntegerStringsRejectAmbiguousSpellings(string spelling)
    {
        var codec = new CheckpointValueCodec();
        JsonElement encoded = JsonSerializer.SerializeToElement(spelling);
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<long>(encoded));
    }

    [Fact]
    public void ObjectsRequireEveryDeclaredMemberAndRejectUnknownMembers()
    {
        var codec = new CheckpointValueCodec();
        codec.Object<IdSequenceCheckpoint>(["NextValue"]);
        using JsonDocument missing = JsonDocument.Parse("{}");
        using JsonDocument extra = JsonDocument.Parse("{\"nextValue\":\"1\",\"typo\":true}");
        Assert.Equal("$.nextValue", Assert.Throws<CheckpointJsonException>(() =>
            codec.Decode<IdSequenceCheckpoint>(missing.RootElement)).Path);
        Assert.Equal("$.typo", Assert.Throws<CheckpointJsonException>(() =>
            codec.Decode<IdSequenceCheckpoint>(extra.RootElement)).Path);
        Assert.Null(codec.Decode<IdSequenceCheckpoint>(codec.Encode(new IdSequenceCheckpoint(null))).NextValue);
    }

    [Fact]
    public void UnionTokensAreExplicitAndClrTypeNamesAreRejected()
    {
        var codec = new CheckpointValueCodec();
        codec.Union<GameEvent>([("economic-reevaluation", typeof(GameEvent.EconomicReevaluation))]);
        codec.Object<GameEvent.EconomicReevaluation>([]);
        JsonElement encoded = codec.Encode<GameEvent>(new GameEvent.EconomicReevaluation());
        Assert.Equal("economic-reevaluation", encoded.GetProperty("kind").GetString());
        Assert.IsType<GameEvent.EconomicReevaluation>(codec.Decode<GameEvent>(encoded));
        using JsonDocument invalid = JsonDocument.Parse("{\"kind\":\"EconomicReevaluation\"}");
        Assert.Equal("$.kind", Assert.Throws<CheckpointJsonException>(() => codec.Decode<GameEvent>(invalid.RootElement)).Path);
    }
}
