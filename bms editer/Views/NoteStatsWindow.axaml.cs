using Avalonia.Controls;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 키음 목록의 클릭 처리는 여기 없다. ListBox 의 SelectedItem 이
// NoteStatsViewModel.SelectedWavStat 로 바로 들어가고, 거기서 격자 선택이 일어난다.
//
// 예전에는 줄마다 Button 을 만들어 명령을 물려야 했는데, 그 배선이 조용히 끊어져
// 눌러도 아무 일이 없었다. 중간 단계를 없애서 끊어질 자리 자체를 지웠다.
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
