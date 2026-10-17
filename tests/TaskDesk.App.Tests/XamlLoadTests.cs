using System.Collections;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskDesk.App.Dialogs;
using TaskDesk.App.Views;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App.Tests;

/// <summary>
/// Smoke-loads the XAML of TaskDesk.App: App.xaml's resources, every window and view, and every style
/// dictionary. XAML compiles even when it fails at load time (a wrong-type resource, a missing
/// StaticResource key, an event handler inside a Style setter); these tests make the gate see it.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class XamlLoadTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(1280, 800);

    // Every Window and UserControl of the App project, built with the smallest data context it needs.
    internal static readonly Dictionary<string, Func<FrameworkElement>> Elements = new()
    {
        [nameof(MainWindow)] = () => new MainWindow(NewMainViewModel(), NewListsViewModel()),
        [nameof(QuickAddWindow)] = () => new QuickAddWindow(new QuickAddViewModel(Stub.Of<ITaskCreator>(), Stub.Of<IListService>())),
        [nameof(SettingsWindow)] = () => new SettingsWindow(NewSettingsViewModel()),
        [nameof(FluentDialog)] = () => new FluentDialog(
            new FluentDialogRequest("Rename list", "Pick a new name.", ["Rename"])
            {
                Input = new FluentDialogInput("Name", "Inbox"),
            }),
        [nameof(DetailPane)] = () => new DetailPane { DataContext = NewMainViewModel() },
        [nameof(NavigationPane)] = () => new NavigationPane { DataContext = NewMainViewModel() },
        [nameof(TaskListArea)] = () => new TaskListArea { DataContext = NewMainViewModel() },
    };

    public static TheoryData<string> ElementNames => [.. Elements.Keys.Order()];

    // The compiled style dictionaries (styles/*.baml in the App assembly), so a new one is covered too.
    public static TheoryData<string> StyleDictionaries => [.. CompiledXaml().Where(n => n.StartsWith("styles/", StringComparison.Ordinal)).Order()];

    [Fact]
    public void Every_window_and_view_has_a_load_test()
    {
        var views = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(Window)) || t.IsSubclassOf(typeof(UserControl))))
            .Select(t => t.Name)
            .Order();

        Assert.Equal(views, Elements.Keys.Order());
    }

    [Fact]
    public void Style_dictionaries_are_found()
    {
        Assert.Contains("styles/foundation.xaml", StyleDictionaries.Select(row => row.Data));
    }

    [Fact]
    public void App_resources_resolve() => fixture.Run(() => ForceResolve(fixture.App.Resources, "App.xaml"));

    [Theory]
    [MemberData(nameof(StyleDictionaries))]
    public void Style_dictionary_resolves(string path) => fixture.Run(() =>
    {
        var dictionary = (ResourceDictionary)Application.LoadComponent(
            new Uri($"/TaskDesk.App;component/{path}", UriKind.Relative));
        ForceResolve(dictionary, path);
    });

    [Theory]
    [MemberData(nameof(ElementNames))]
    public void Window_or_view_loads_and_lays_out(string name) => fixture.Run(() =>
    {
        var element = Elements[name]();
        try
        {
            ForceResolve(element.Resources, name);
            // A window that was never shown skips layout, so lay out its template root instead: that
            // applies the templates of everything inside it, as showing the window would.
            Assert.True(element.ApplyTemplate() || VisualTreeHelper.GetChildrenCount(element) > 0,
                $"{name} built no visual tree.");
            var root = (UIElement)VisualTreeHelper.GetChild(element, 0);
            root.Measure(LayoutSize);
            root.Arrange(new Rect(LayoutSize));
            root.UpdateLayout();
            Assert.Empty(UntemplatedControls(root));
        }
        finally
        {
            (element as Window)?.Close();
        }
    });

    // Visible controls in the laid-out tree whose template never applied: layout did not reach them.
    // Layout skips a collapsed or hidden element and everything under it, so those are not counted.
    private static List<string> UntemplatedControls(DependencyObject root)
    {
        var missed = new List<string>();
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
            {
                continue;
            }

            var children = VisualTreeHelper.GetChildrenCount(node);
            if (node is Control { Template: not null } control && children == 0)
            {
                missed.Add($"{control.GetType().Name} '{control.Name}'");
            }

            for (var i = 0; i < children; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        return missed;
    }

    private static MainViewModel NewMainViewModel() => new(Stub.Of<ITaskService>(), Stub.Of<ITagService>());

    private static ListsViewModel NewListsViewModel() => new(Stub.Of<IListService>(), Stub.Of<IListDialogs>());

    private static SettingsViewModel NewSettingsViewModel()
    {
        var store = new InMemorySettingsStore();
        return new SettingsViewModel(
            store,
            new AutoStartService(Stub.Of<IRunKey>()),
            Stub.Of<IThemeService>(),
            Stub.Of<IFolderOpener>(),
            AppPaths.ForRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TaskDesk-app-tests")),
            exePath: "",
            version: "0.0.0",
            new HotkeyService(Stub.Of<IHotkeyRegistrar>(), store));
    }

    // Reads every key (deferred values load on first access, which is where a bad resource throws), then
    // instantiates every template, including those set by a Style, so the resources inside them resolve too.
    private static void ForceResolve(ResourceDictionary dictionary, string source)
    {
        // Only TaskDesk's own dictionaries: ThemeMode merges Fluent's, which are not ours to check.
        foreach (var merged in dictionary.MergedDictionaries.Where(IsOwn))
        {
            ForceResolve(merged, merged.Source.OriginalString);
        }

        foreach (var key in dictionary.Keys.Cast<object>().ToList())
        {
            object? value;
            try
            {
                value = dictionary[key];
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Resource '{key}' in {source} failed to load.", ex);
            }

            try
            {
                LoadTemplates(value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"A template in resource '{key}' in {source} failed to load.", ex);
            }
        }
    }

    private static bool IsOwn(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Contains("TaskDesk.App;component/", StringComparison.OrdinalIgnoreCase) == true
        || dictionary.Source?.IsAbsoluteUri == false && !dictionary.Source.OriginalString.Contains(";component/", StringComparison.Ordinal);

    private static void LoadTemplates(object? value)
    {
        switch (value)
        {
            case FrameworkTemplate template:
                template.LoadContent();
                break;
            case Style style:
                foreach (var setter in style.Setters.OfType<Setter>())
                {
                    LoadTemplates(setter.Value);
                }

                break;
        }
    }

    private static IEnumerable<string> CompiledXaml()
    {
        var assembly = typeof(App).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.g.resources")
            ?? throw new InvalidOperationException("The App assembly has no compiled XAML resources.");
        using var reader = new ResourceReader(stream);
        return reader.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(name => name.EndsWith(".baml", StringComparison.Ordinal))
            .Select(name => name[..^".baml".Length] + ".xaml")
            .ToList();
    }
}
