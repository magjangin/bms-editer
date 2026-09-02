using Avalonia.Controls;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 집계를 보여주기만 하는 창이다. 누르는 것은 아무것도 없다.
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
