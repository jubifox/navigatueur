using Navigatueur.Core.Settings;
using Xunit;

namespace Navigatueur.Core.Tests;

public class AppSettingsSerializerTests
{
    [Fact]
    public void Deserialize_EmptyString_ReturnsDefaults()
    {
        var settings = AppSettingsSerializer.Deserialize(string.Empty);

        Assert.Equal(new AppSettings().HomePageUrl, settings.HomePageUrl);
        Assert.Equal(new AppSettings().WindowWidth, settings.WindowWidth);
        Assert.Null(settings.WindowLeft);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsValues()
    {
        var original = new AppSettings
        {
            HomePageUrl = "https://example.com",
            WindowWidth = 1024,
            WindowHeight = 768,
            WindowLeft = 50,
            WindowTop = 75,
        };

        var json = AppSettingsSerializer.Serialize(original);
        var roundTripped = AppSettingsSerializer.Deserialize(json);

        Assert.Equal(original.HomePageUrl, roundTripped.HomePageUrl);
        Assert.Equal(original.WindowWidth, roundTripped.WindowWidth);
        Assert.Equal(original.WindowHeight, roundTripped.WindowHeight);
        Assert.Equal(original.WindowLeft, roundTripped.WindowLeft);
        Assert.Equal(original.WindowTop, roundTripped.WindowTop);
    }

    [Fact]
    public void Default_HomePageUrl_IsNotEmpty()
    {
        var settings = new AppSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings.HomePageUrl));
    }
}
