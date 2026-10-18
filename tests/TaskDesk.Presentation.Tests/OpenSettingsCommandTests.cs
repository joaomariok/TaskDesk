using Microsoft.Extensions.Time.Testing;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class OpenSettingsCommandTests
{
    [Fact]
    public void Ctrl_comma_command_raises_SettingsRequested_once_per_run()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        var raised = 0;
        vm.SettingsRequested += (_, _) => raised++;

        vm.OpenSettingsCommand.Execute(null);
        vm.OpenSettingsCommand.Execute(null);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Command_with_no_subscriber_does_nothing()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());

        vm.OpenSettingsCommand.Execute(null);
    }
}
