using System.Text.Json;
using System.Text.Json.Serialization;
using Daktela.Connector.Serialization;
using Xunit;

namespace Daktela.Connector.Tests;

public class SerializationTests
{
    private readonly JsonSerializerOptions _options = DaktelaJson.CreateOptions(null);

    [Fact]
    public void DateTime_ReadsDaktelaAndIsoFormats()
    {
        var daktela = JsonSerializer.Deserialize<DateTime>(
            "\"2026-08-19 13:14:15\"",
            _options);
        var iso = JsonSerializer.Deserialize<DateTime>(
            "\"2026-08-19T13:14:15Z\"",
            _options);

        Assert.Equal(new DateTime(2026, 8, 19, 13, 14, 15), daktela);
        Assert.Equal(DateTimeKind.Unspecified, daktela.Kind);
        Assert.Equal(DateTimeKind.Utc, iso.Kind);
    }

    [Fact]
    public void DateTime_WritesDaktelaFormat()
    {
        var json = JsonSerializer.Serialize(
            new DateTime(2026, 8, 19, 13, 14, 15, DateTimeKind.Unspecified),
            _options);

        Assert.Equal("\"2026-08-19 13:14:15\"", json);
    }

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("123")]
    public void DateTime_WithUnsupportedValue_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>(json, _options));
    }

    [Fact]
    public void DateTimeOffset_ReadsDaktelaFormatsWithAndWithoutOffset()
    {
        var withOffset = JsonSerializer.Deserialize<DateTimeOffset>(
            "\"2026-08-19 13:14:15 +02:00\"",
            _options);
        var withoutOffset = JsonSerializer.Deserialize<DateTimeOffset>(
            "\"2026-08-19 13:14:15\"",
            _options);
        var iso = JsonSerializer.Deserialize<DateTimeOffset>(
            "\"2026-08-19T13:14:15+02:00\"",
            _options);

        Assert.Equal(TimeSpan.FromHours(2), withOffset.Offset);
        Assert.Equal(new DateTime(2026, 8, 19, 13, 14, 15), withoutOffset.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), iso.Offset);
    }

    [Fact]
    public void DateTimeOffset_WritesServerLocalDaktelaFormat()
    {
        var json = JsonSerializer.Serialize(
            new DateTimeOffset(2026, 8, 19, 13, 14, 15, TimeSpan.FromHours(2)),
            _options);

        Assert.Equal("\"2026-08-19 13:14:15\"", json);
    }

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("true")]
    public void DateTimeOffset_WithUnsupportedValue_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>(json, _options));
    }

    [Fact]
    public void CreateOptions_CopiesCallerOptionsAndReadsNumbersFromStrings()
    {
        var configured = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        var options = DaktelaJson.CreateOptions(configured);
        var model = JsonSerializer.Deserialize<NumberDto>("""{"count":"42"}""", options);

        Assert.NotSame(configured, options);
        Assert.False(configured.PropertyNameCaseInsensitive);
        Assert.Equal(JsonIgnoreCondition.Never, configured.DefaultIgnoreCondition);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(42, model?.Count);
    }

    [Fact]
    public void ParseErrors_HandlesCanonicalAndScalarValues()
    {
        using var document = JsonDocument.Parse(
            """{"error":[{"message":"Bad field","field":"name","code":17},true,42]}""");

        var errors = DaktelaJson.ParseErrors(document.RootElement);

        Assert.Equal(3, errors.Count);
        Assert.Equal("name", errors[0].Field);
        Assert.Equal("17", errors[0].Code);
        Assert.Equal("True", errors[1].Message);
        Assert.Equal("42", errors[2].Message);
    }

    private sealed class NumberDto
    {
        public int Count { get; set; }
    }
}
