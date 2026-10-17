using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TaskDesk.App.Views;
using TaskDesk.Core.Tags;
using TaskDesk.Presentation;

namespace TaskDesk.App.Tests;

/// <summary>
/// A toggle button that does its work through a Command must run that Command when a screen reader
/// toggles it (UI Automation's Toggle pattern), not just flip its own look.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class CommandToggleTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(1280, 800);

    public static TheoryData<string> ElementNames => XamlLoadTests.ElementNames;

    [Theory]
    [MemberData(nameof(ElementNames))]
    public void Screen_reader_toggle_runs_the_command_of_every_command_driven_toggle(string name) => fixture.Run(() =>
    {
        WithLaidOut(name, toggles =>
        {
            foreach (var button in toggles)
            {
                var label = Describe(button);
                var command = new RecordingCommand();
                button.Command = command;
                var before = button.IsChecked;

                var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button)
                    .GetPattern(PatternInterface.Toggle);
                toggle.Toggle();

                Assert.True(command.Executions.Count == 1, $"Toggling '{label}' in {name} ran its command {command.Executions.Count} times.");
                Assert.Equal(button.CommandParameter, command.Executions[0]);
                // The command did not change the bound state, so the button must not look changed either.
                Assert.True(before == button.IsChecked, $"Toggling '{label}' in {name} flipped it without its command.");
            }
        });
    });

    [Fact]
    public void Main_window_command_driven_toggles_are_all_found() => fixture.Run(() =>
    {
        WithLaidOut(nameof(MainWindow), toggles =>
            Assert.Equal(["Filter", "Has due filter", "Has reminder filter", "work"], toggles.Select(Describe).Order()));
    });

    [Fact]
    public void Screen_reader_toggles_the_Filter_row_and_reads_back_its_state() => fixture.Run(() =>
    {
        WithLaidOut(nameof(TaskListArea), toggles =>
        {
            var button = toggles.Single(b => AutomationProperties.GetAutomationId(b) == "FilterButton");
            var viewModel = (MainViewModel)button.DataContext;
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button)
                .GetPattern(PatternInterface.Toggle);

            // The row starts open here, so the first toggle closes it.
            toggle.Toggle();

            Assert.False(viewModel.IsFilterBarOpen);
            Assert.Equal(ToggleState.Off, toggle.ToggleState);

            toggle.Toggle();

            Assert.True(viewModel.IsFilterBarOpen);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
        });
    });

    [Fact]
    public void Command_driven_toggles_wear_the_app_toggle_button_style() => fixture.Run(() =>
    {
        WithLaidOut(nameof(TaskListArea), toggles =>
        {
            var style = fixture.App.FindResource(typeof(ToggleButton));
            Assert.All(toggles, button => Assert.Same(style, button.Style));
        });
    });

    // Builds the element with the Filter row open and one Tag chip, lays it out, and hands over every
    // toggle button in it that has a Command. CheckBoxes are left out: the Task "Completed" boxes bind
    // IsChecked two-way and are a separate case.
    private static void WithLaidOut(string name, Action<List<ToggleButton>> act)
    {
        var element = XamlLoadTests.Elements[name]();
        try
        {
            if (element.DataContext is MainViewModel viewModel)
            {
                viewModel.IsFilterBarOpen = true;
                viewModel.FilterTagChips.Add(new FilterTagChip(new Tag { Id = Guid.NewGuid(), Name = "work" }, false));
            }

            element.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(element) > 0)
            {
                var root = (UIElement)VisualTreeHelper.GetChild(element, 0);
                root.Measure(LayoutSize);
                root.Arrange(new Rect(LayoutSize));
                root.UpdateLayout();
            }

            act([.. Descendants(element).OfType<ToggleButton>().Where(b => b is not CheckBox && b.Command is not null)]);
        }
        finally
        {
            (element as Window)?.Close();
        }
    }

    private static string Describe(ToggleButton button)
    {
        var name = AutomationProperties.GetName(button);
        return string.IsNullOrEmpty(name)
            ? UIElementAutomationPeer.CreatePeerForElement(button).GetName()
            : name;
    }

    // The visual and the logical tree together, so nothing is missed whether layout reached it or not.
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            yield return node;
            if (node is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    pending.Push(VisualTreeHelper.GetChild(node, i));
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                pending.Push(child);
            }
        }
    }

    private sealed class RecordingCommand : ICommand
    {
        public List<object?> Executions { get; } = [];

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Executions.Add(parameter);
    }
}
