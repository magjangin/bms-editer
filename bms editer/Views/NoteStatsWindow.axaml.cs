using Avalonia.Controls;
using bms_editer.ViewModels;

namespace bms_editer.Views;

public partial class NoteStatsWindow : Window
{
    public NoteStatsWindow()
    {
        InitializeComponent();
    }

    public NoteStatsWindow(NoteStatsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
