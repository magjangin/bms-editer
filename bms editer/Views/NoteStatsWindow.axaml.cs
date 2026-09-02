using Avalonia.Controls;
using Avalonia.Interactivity;
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

    // 키음 목록의 줄을 눌렀을 때.
    //
    // 예전에는 XAML 에서 `{Binding $parent[ItemsControl].((vm:NoteStatsViewModel)DataContext).…}` 로
    // 명령을 끌어왔는데, 이 프로젝트는 컴파일된 바인딩(AvaloniaUseCompiledBindingsByDefault)을 쓴다.
    // 조상을 타고 올라가 캐스팅까지 하는 경로는 실패해도 예외가 나지 않고 **조용히 무시된다.**
    // 그래서 눌러도 아무 일이 없었다. 누른 줄의 DataContext 에서 직접 꺼내 쓴다.
    private void OnWavStatRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: WavNoteStat stat })
            return;

        if (DataContext is not NoteStatsViewModel viewModel)
            return;

        viewModel.SelectByWavKeyCommand.Execute(stat.Key);
    }
}
