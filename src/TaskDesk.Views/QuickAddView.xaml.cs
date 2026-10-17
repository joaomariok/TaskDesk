using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>The quick-add window's content. Enter saves, Escape cancels; the view model closes the window.</summary>
public sealed partial class QuickAddView : UserControl
{
    public QuickAddView(QuickAddViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public QuickAddViewModel ViewModel { get; }

    /// <summary>Gives the title box keyboard focus, as the card does when it opens.</summary>
    public void FocusTitle() => TitleBox.Focus(FocusState.Programmatic);

    /// <summary>
    /// The card's keys: Enter adds the Task, Escape dismisses the card. Returns false for any other key, which
    /// stays with the focused control. Keys an open drop-down handles never reach the card.
    /// </summary>
    public bool HandleKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Enter:
                // TextChanged (and so the binding) runs after the key that changed the text; save what the box shows.
                ViewModel.Title = TitleBox.Text;
                if (ViewModel.SaveCommand.CanExecute(null))
                {
                    ViewModel.SaveCommand.Execute(null);
                }

                return true;
            case VirtualKey.Escape:
                ViewModel.CancelCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = HandleKey(e.Key);

    // The picker gives a DateTimeOffset for the picked day; the view model keeps the local calendar date only.
    private void OnDueChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        ViewModel.DueDate = args.NewDate?.LocalDateTime.Date;
}
