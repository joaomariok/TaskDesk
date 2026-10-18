using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class WindowLayoutTests
{
    // Screen is 0,0 1920x1080.
    [Theory]
    [InlineData(100, 100, 1000, 700, 100, 100, 1000, 700)]     // already fits
    [InlineData(100, 100, 300, 200, 100, 100, 800, 500)]       // below minimum size
    [InlineData(5000, 100, 1000, 700, 920, 100, 1000, 700)]    // off-screen right
    [InlineData(-3000, -500, 1000, 700, 0, 0, 1000, 700)]     // off-screen left/top
    [InlineData(0, 0, 4000, 3000, 0, 0, 1920, 1080)]           // larger than screen
    public void FitTo_keeps_minimum_size_and_stays_on_screen(
        double left, double top, double width, double height,
        double expLeft, double expTop, double expWidth, double expHeight)
    {
        var fitted = new WindowLayout(left, top, width, height).FitTo(0, 0, 1920, 1080);

        Assert.Equal((expLeft, expTop, expWidth, expHeight), (fitted.Left, fitted.Top, fitted.Width, fitted.Height));
    }

    [Fact]
    public void FitTo_respects_a_screen_that_does_not_start_at_the_origin()
    {
        var fitted = new WindowLayout(0, 0, 1000, 700).FitTo(-1920, 0, 1920, 1080);

        Assert.Equal(-1000, fitted.Left);
    }

    // ---- Pane widths (sidebar and detail pane) ----

    [Fact]
    public void Without_a_saved_layout_the_panes_have_their_default_widths()
    {
        Assert.Equal(180, PaneLayout.WidthOf(null, PaneLayout.Navigation));
        Assert.Equal(320, PaneLayout.WidthOf(null, PaneLayout.Detail));
        Assert.Equal(320, PaneLayout.WidthOf(new WindowLayout(0, 0, 1000, 700), PaneLayout.Detail));
    }

    [Theory]
    [InlineData(PaneLayout.Navigation, 250, 250)]  // in range
    [InlineData(PaneLayout.Navigation, 50, 140)]   // below the minimum
    [InlineData(PaneLayout.Navigation, 900, 400)]  // above the maximum
    [InlineData(PaneLayout.Detail, 300, 320)]
    [InlineData(PaneLayout.Detail, 100, 320)]
    [InlineData(PaneLayout.Detail, 2000, 600)]
    [InlineData(PaneLayout.Detail, 0, 320)]        // nothing usable saved: the default
    [InlineData(PaneLayout.Detail, -5, 320)]
    public void A_saved_width_is_restored_within_the_pane_limits(string pane, double saved, double expected)
    {
        var layout = new WindowLayout(0, 0, 1000, 700, PaneWidths: new Dictionary<string, double> { [pane] = saved });

        Assert.Equal(expected, PaneLayout.WidthOf(layout, pane));
    }

    [Fact]
    public void The_limits_keep_each_default_inside_them()
    {
        foreach (var pane in new[] { PaneLayout.Navigation, PaneLayout.Detail })
        {
            var limits = PaneLayout.LimitsOf(pane);
            Assert.InRange(limits.Default, limits.Min, limits.Max);
        }
    }

    [Fact]
    public void Updating_clamps_measured_widths_and_keeps_the_others()
    {
        var saved = new Dictionary<string, double> { [PaneLayout.Detail] = 300, ["other"] = 42 };

        var updated = PaneLayout.Update(saved, new Dictionary<string, double>
        {
            [PaneLayout.Navigation] = 1000,
            [PaneLayout.Detail] = 350,
        });

        Assert.Equal(400, updated[PaneLayout.Navigation]);
        Assert.Equal(350, updated[PaneLayout.Detail]);
        Assert.Equal(42, updated["other"]);
    }

    [Fact]
    public void A_hidden_pane_keeps_its_saved_width()
    {
        var saved = new Dictionary<string, double> { [PaneLayout.Detail] = 330 };

        var updated = PaneLayout.Update(saved, new Dictionary<string, double>
        {
            [PaneLayout.Navigation] = 200,
            [PaneLayout.Detail] = 0, // collapsed while no Task is selected
        });

        Assert.Equal(330, updated[PaneLayout.Detail]);
        Assert.Equal(200, updated[PaneLayout.Navigation]);
    }

    [Fact]
    public void Pane_widths_round_trip_through_the_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "taskdesk-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new JsonShellSettingsStore(path);
            var widths = PaneLayout.Update(null, new Dictionary<string, double>
            {
                [PaneLayout.Navigation] = 210,
                [PaneLayout.Detail] = 340,
            });
            store.Save(new ShellSettings(Layout: new WindowLayout(10, 10, 1000, 700, PaneWidths: widths)));

            var loaded = store.Load().Layout;

            Assert.Equal(210, PaneLayout.WidthOf(loaded, PaneLayout.Navigation));
            Assert.Equal(340, PaneLayout.WidthOf(loaded, PaneLayout.Detail));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
