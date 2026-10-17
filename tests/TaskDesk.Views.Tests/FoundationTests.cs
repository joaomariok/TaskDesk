using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using static TaskDesk.Views.Tests.ThemeColours;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The theme foundation (<c>Styles/Foundation.xaml</c>): the design tokens every view builds on, and the
/// meaning-named brushes in theme dictionaries for Light, Dark and high contrast.
/// </summary>
[TestClass]
public sealed class FoundationTests
{
    private static readonly string[] Tokens =
    [
        "CaptionText", "BodyText", "BodyStrongText", "SubtitleText", "TitleText", "SecondaryCaptionText",
        "FieldLabel", "FieldLabelMargin", "FieldMargin", "OverdueBrush", "TodayBrush", "ListStripeBrush",
    ];

    private static readonly string[] MeaningBrushes = ["OverdueBrush", "TodayBrush", "ListStripeBrush"];

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public void Every_token_resolves_from_the_app_resources()
    {
        var missing = Tokens.Where(key => !Application.Current.Resources.TryGetValue(key, out var value) || value is null).ToList();

        Assert.IsEmpty(missing, $"Tokens not found in the app resources: {string.Join(", ", missing)}");
    }

    [UITestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    [DataRow("HighContrast")]
    public void Every_meaning_brush_resolves_in_the_theme(string theme)
    {
        var foundation = Foundation();
        Assert.IsTrue(foundation.ThemeDictionaries.TryGetValue(theme, out var dictionary),
            $"The foundation has no {theme} theme dictionary.");

        var unresolved = MeaningBrushes
            .Where(key => !((ResourceDictionary)dictionary).TryGetValue(key, out var value) || value is not Brush)
            .ToList();

        Assert.IsEmpty(unresolved, $"{theme}: brushes that do not resolve: {string.Join(", ", unresolved)}");
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light, 0xFFC42B1Cu, 0x72000000u)]
    [DataRow(ElementTheme.Dark, 0xFFFF99A4u, 0x8BFFFFFFu)]
    public async Task Meaning_brushes_take_their_Fluent_colours_in_the_requested_theme(
        ElementTheme theme, uint overdue, uint listStripe)
    {
        var colours = await ResolveAsync(
            theme, ["OverdueBrush", "TodayBrush", "ListStripeBrush", "AccentTextFillColorPrimaryBrush"],
            TestContext.CancellationToken);

        Assert.AreEqual(Rgb(overdue), colours["OverdueBrush"], "OverdueBrush is Fluent's critical colour.");
        Assert.AreEqual(Rgb(listStripe), colours["ListStripeBrush"], "ListStripeBrush is Fluent's strong stroke.");
        Assert.AreEqual(colours["AccentTextFillColorPrimaryBrush"], colours["TodayBrush"],
            "TodayBrush is the accent in Fluent's readable accent-text shade.");
    }

    // Re-expresses the WPF App tests' contrast check (RailTests.Rail_badge_text_reads_at_body_text_contrast,
    // through its FluentTheme helper) for the foundation's own text tokens.
    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task Overdue_and_secondary_text_read_at_body_text_contrast_on_the_window(ElementTheme theme)
    {
        var colours = await ResolveAsync(
            theme, ["SolidBackgroundFillColorBaseBrush", "OverdueBrush", "SecondaryCaptionText"],
            TestContext.CancellationToken);
        var window = colours["SolidBackgroundFillColorBaseBrush"];

        foreach (var key in new[] { "OverdueBrush", "SecondaryCaptionText" })
        {
            var ink = Over(colours[key], window);
            Assert.IsGreaterThanOrEqualTo(4.5, Contrast(ink, window),
                $"{theme}: {key} text contrast is {Contrast(ink, window):0.00}:1");
        }
    }

    private static ResourceDictionary Foundation() =>
        Application.Current.Resources.MergedDictionaries.SingleOrDefault(d =>
            d.Source?.OriginalString.EndsWith("/Styles/Foundation.xaml", StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new AssertFailedException("Styles/Foundation.xaml is not merged into the app resources.");
}
