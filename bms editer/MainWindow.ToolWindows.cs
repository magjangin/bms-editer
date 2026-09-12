using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using bms_editer.ViewModels;
using bms_editer.Views;

namespace bms_editer;

public partial class MainWindow
{
        // 지금 떠 있는 모드리스 보조 창(검색/통계/키음 팔레트). 종류당 하나만 띄운다.
        private readonly Dictionary<Type, Window> _toolWindows = new();

        // 검색 결과를 격자에서 바로 확인해야 하므로 모달이 아닌 모드리스로 띄운다.
        private void OnShowNoteSearchClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new NoteSearchWindow(new NoteSearchViewModel(vm)));

        // 편집하는 동안 집계가 따라 움직여야 하므로 검색 창과 같이 모드리스로 띄운다.
        // 숫자만 보는 창이다. 골라서 다루는 쪽은 아래 컨트롤 패널이 맡는다.
        private void OnShowStatsClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new NoteStatsWindow(new NoteStatsViewModel(vm)));

        // 같은 집계를 물려받아 레인/키음별 일괄 선택·미리듣기·일괄 교체까지 잇는 공구함.
        // 여기서 고른 노트를 격자에서 이어 손보므로 역시 모드리스로 띄운다.
        private void OnShowControlPanelClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new ControlPanelWindow(new ControlPanelViewModel(vm)));

        // 사이드바의 좁은 키음 목록 대신 넓은 타일 판에서 고르는 창.
        // 선택이 곧 편집용 붓이므로 팔레트를 열 때 편집 모드(연필 아이콘)를 즉시 활성화한다.
        private void OnShowWavPaletteClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.IsEditMode = true;

            ShowToolWindow(vm => new WavPaletteWindow(new WavPaletteViewModel(vm)));
        }

        // 모드리스 보조 창을 종류당 하나만 띄운다. 이미 떠 있으면 앞으로 가져온다.
        //
        // 보조 창의 뷰모델은 메인 뷰모델의 변경 알림을 구독하므로, 창이 닫힐 때
        // 반드시 해제해야 닫은 창이 계속 살아남지 않는다. 세 창이 같은 절차를
        // 따로 복사해 갖고 있었는데, 하나라도 Dispose 를 빠뜨리면 조용히 새는 자리였다.
        private void ShowToolWindow<TWindow>(Func<MainWindowViewModel, TWindow> create)
            where TWindow : Window
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (_toolWindows.TryGetValue(typeof(TWindow), out var existing))
            {
                existing.Activate();
                return;
            }

            var window = create(vm);
            window.Closed += (_, _) =>
            {
                _toolWindows.Remove(typeof(TWindow));
                (window.DataContext as IDisposable)?.Dispose();
            };

            _toolWindows[typeof(TWindow)] = window;
            window.Show(this);
        }
    }
