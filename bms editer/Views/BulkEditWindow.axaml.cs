using Avalonia.Controls;
using bms_editer.ViewModels;

namespace bms_editer.Views;

public partial class BulkEditWindow : Window
{
    public BulkEditWindow()
    {
        InitializeComponent();
    }

    public BulkEditWindow(BulkEditViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
