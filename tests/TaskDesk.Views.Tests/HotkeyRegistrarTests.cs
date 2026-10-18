using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;
using TaskDesk.Core.Shell;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The WinUI shell's global hotkey: Win32 RegisterHotKey on a message-only window that the UI thread's message
/// loop serves. The shell sources are linked into this test app. The registrations go to a fake of the session's
/// hotkey table (<see cref="FakeHotkeyTable"/>): the real one is shared with every app, so the gate never registers
/// a real hotkey. The UI suite's GlobalHotkeyTests and QuickAddTests register real ones.
/// </summary>
[TestClass]
public sealed partial class HotkeyRegistrarTests
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private static readonly Hotkey Unused = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x79); // F10
    private static readonly Hotkey OtherUnused = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x78); // F9

    private readonly FakeHotkeyTable _hotkeyTable = new();

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public void A_hotkey_registers_its_combination_on_the_message_window_without_key_repeat()
    {
        using var window = new MessageWindow();
        using var registrar = new WinUiHotkeyRegistrar(window, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);

        Assert.IsTrue(registrar.TryRegister(Unused));

        // MOD_CONTROL | MOD_ALT | MOD_SHIFT, with MOD_NOREPEAT so holding the keys opens quick add once.
        var registration = _hotkeyTable.Registrations.Single();
        Assert.AreEqual((window.Handle, 0x0002u | 0x0001u | 0x0004u | 0x4000u, 0x79u),
            (registration.Window, registration.Modifiers, registration.Key));
    }

    [UITestMethod]
    public async Task A_message_posted_to_the_message_window_reaches_its_hooks_on_the_UI_thread()
    {
        var ct = TestContext.CancellationToken;
        using var window = new MessageWindow();
        var seen = new List<(uint Message, nint WParam)>();
        window.AddHook((uint message, nint wParam, nint lParam, out nint result) =>
        {
            result = 0;
            if (message != WmApp + 7)
            {
                return false;
            }

            seen.Add((message, wParam));
            return true;
        });

        Assert.IsTrue(PostMessageW(window.Handle, WmApp + 7, 42, 0));

        await UiWait.UntilAsync(() => seen.Count == 1, () => "The posted message did not reach the hook.", ct);
        Assert.AreEqual((WmApp + 7, (nint)42), seen.Single());
    }

    [UITestMethod]
    public async Task A_hotkey_message_for_the_registered_hotkey_raises_Pressed_once()
    {
        var ct = TestContext.CancellationToken;
        using var window = new MessageWindow();
        using var registrar = new WinUiHotkeyRegistrar(window, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        var pressed = 0;
        registrar.Pressed += () => pressed++;
        Assert.IsTrue(registrar.TryRegister(Unused));

        // WM_HOTKEY carries the registration id; only the current one counts, whichever of the two it is.
        Assert.IsTrue(PostMessageW(window.Handle, WmHotkey, 1, 0));
        Assert.IsTrue(PostMessageW(window.Handle, WmHotkey, 2, 0));

        await UiWait.UntilAsync(() => pressed == 1, () => $"Pressed was raised {pressed} times.", ct);
        await Task.Delay(100, ct);
        Assert.AreEqual(1, pressed);
    }

    [UITestMethod]
    public void A_hotkey_another_registration_owns_is_refused_and_the_working_one_stays()
    {
        using var window = new MessageWindow();
        using var otherWindow = new MessageWindow();
        using var owner = new WinUiHotkeyRegistrar(window, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        using var registrar = new WinUiHotkeyRegistrar(otherWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        Assert.IsTrue(owner.TryRegister(Unused));
        Assert.IsTrue(registrar.TryRegister(OtherUnused));

        Assert.IsFalse(registrar.TryRegister(Unused), "The combination is taken.");

        // The registrar still holds its previous hotkey: nobody else can take it.
        Assert.IsFalse(owner.TryRegister(OtherUnused), "The previous hotkey must stay registered.");
    }

    [UITestMethod]
    public void Unregister_frees_the_combination()
    {
        using var firstWindow = new MessageWindow();
        using var secondWindow = new MessageWindow();
        using var first = new WinUiHotkeyRegistrar(firstWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        using var second = new WinUiHotkeyRegistrar(secondWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        Assert.IsTrue(first.TryRegister(Unused));

        first.Unregister();

        Assert.IsTrue(second.TryRegister(Unused));
    }

    [UITestMethod]
    public void Switching_hotkeys_releases_the_old_one()
    {
        using var firstWindow = new MessageWindow();
        using var secondWindow = new MessageWindow();
        using var first = new WinUiHotkeyRegistrar(firstWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        using var second = new WinUiHotkeyRegistrar(secondWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        Assert.IsTrue(first.TryRegister(Unused));

        Assert.IsTrue(first.TryRegister(OtherUnused));

        Assert.IsTrue(second.TryRegister(Unused));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint message, nint wParam, nint lParam);
}
