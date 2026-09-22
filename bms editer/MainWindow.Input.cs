using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.ViewModels;

namespace bms_editer;

public partial class MainWindow
{
        private void OnNoteGridKeyDown(object? sender, KeyEventArgs e) => HandleEditorKey(e);

        // 창 전체에서 받는 단축키.
        //
        // 예전에는 Delete·방향키가 **격자에 포커스가 있을 때만** 먹었다. 팔레트나 사이드바를
        // 한 번 만지면 조용히 안 먹었고 안내도 없었다. 이제 창이 받아준다.
        // 격자가 먼저 처리했으면 e.Handled 가 서 있어 두 번 돌지 않는다.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || DataContext is not MainWindowViewModel vm)
                return;

            var focused = FocusManager?.GetFocusedElement();

            // 글자를 치는 중이면 단축키가 아니라 입력이다.
            if (IsWithin<TextBox>(focused))
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                HandleControlShortcut(e);
                return;
            }

            if (e.Key == Key.Space)
            {
                // 스페이스는 포커스가 잡힌 버튼·체크박스를 누르는 키이기도 하다. 그쪽이 우선이다.
                if (IsWithin<Button>(focused) || IsWithin<ToggleButton>(focused))
                    return;

                vm.TogglePlaybackCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // 방향키·Delete 는 목록·콤보·슬라이더에서는 그쪽 것이다.
            if (IsWithin<ListBox>(focused) || IsWithin<ComboBox>(focused)
                || IsWithin<Slider>(focused) || IsWithin<NumericUpDown>(focused))
            {
                return;
            }

            HandleEditorKey(e);
        }

        private void HandleControlShortcut(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    OnSaveFileAsClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    OnSaveFileClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.O:
                    OnOpenFileClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.N:
                    if (DataContext is MainWindowViewModel vm)
                        vm.NewFileCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        // 격자 편집 키(선택 해제·삭제·이동). 격자에서도 창에서도 같은 규칙을 쓴다.
        private void HandleEditorKey(KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            switch (e.Key)
            {
                case Key.Delete:
                    vm.DeleteSelectedNotesCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Key.Escape:
                    vm.ClearNoteSelection();
                    e.Handled = true;
                    return;
            }

            NoteMoveDirection? direction = e.Key switch
            {
                Key.Up => vm.IsHorizontalView ? NoteMoveDirection.LanePrevious : NoteMoveDirection.TimeForward,
                Key.Down => vm.IsHorizontalView ? NoteMoveDirection.LaneNext : NoteMoveDirection.TimeBackward,
                Key.Left => vm.IsHorizontalView ? NoteMoveDirection.TimeBackward : NoteMoveDirection.LanePrevious,
                Key.Right => vm.IsHorizontalView ? NoteMoveDirection.TimeForward : NoteMoveDirection.LaneNext,
                _ => null
            };

            if (direction is { } d)
            {
                vm.MoveSelectedNotesCommand.Execute(d);
                e.Handled = true;
            }
        }

        // 포커스가 T 안에(또는 T 자신에) 있는지.
        private static bool IsWithin<T>(IInputElement? focused) where T : class
        {
            if (focused is not Visual visual)
                return false;

            foreach (var ancestor in visual.GetSelfAndVisualAncestors())
            {
                if (ancestor is T)
                    return true;
            }

            return false;
        }
    }
