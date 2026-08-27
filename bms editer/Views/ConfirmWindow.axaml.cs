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

    public ConfirmWindow(string message, string title = "확인", bool messageOnly = false) : this()
    {
        Title = title;
        MessageText.Text = message;

        // 알림용으로 쓸 때는 고를 게 없으니 취소 버튼을 감춘다.
        if (messageOnly)
            CancelButton.IsVisible = false;
    }

    // 확인을 누르면 true, 취소하거나 창을 닫으면 false를 돌려준다.
    public static async Task<bool> ShowAsync(Window owner, string message, string title = "확인")
    {
        var dialog = new ConfirmWindow(message, title);
        return await dialog.ShowDialog<bool>(owner);
    }

    // 확인 버튼만 있는 알림창. 저장/열기 실패처럼 알리기만 할 때 쓴다.
    public static async Task ShowMessageAsync(Window owner, string message, string title = "알림")
    {
        var dialog = new ConfirmWindow(message, title, messageOnly: true);
        await dialog.ShowDialog<bool>(owner);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
