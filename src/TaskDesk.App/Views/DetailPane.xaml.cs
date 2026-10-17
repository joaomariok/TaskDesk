using System.Windows.Controls;
using TaskDesk.Core.Tasks;

namespace TaskDesk.App.Views;

/// <summary>The detail pane of the selected Task.</summary>
public partial class DetailPane : UserControl
{
    public DetailPane()
    {
        InitializeComponent();
    }

    /// <summary>Fills the Priority choices; called once by <see cref="MainWindow"/>.</summary>
    public void Initialize()
    {
        PriorityBox.ItemsSource = Enum.GetValues<Priority>();
    }

    /// <summary>Focuses the title box with its text selected (F2).</summary>
    public void FocusTitle()
    {
        DetailTitleBox.Focus();
        DetailTitleBox.SelectAll();
    }
}
