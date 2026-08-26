using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace bms_editer.Views;

// 확인/취소 두 개의 버튼만 가진 공용 확인 대화상자.
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string message, string title = "확인") : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    // 확인을 누르면 true, 취소하거나 창을 닫으면 false를 돌려준다.
    public static async Task<bool> ShowAsync(Window owner, string message, string title = "확인")
    {
        var dialog = new ConfirmWindow(message, title);
        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
