using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using bms_editer.ViewModels;

namespace bms_editer.Views;

internal static class ToolWindowInputHelper
{
    public static bool IsWithin<T>(IInputElement? element) where T : class =>
        element is Visual visual && visual.GetSelfAndVisualAncestors().OfType<T>().Any();

    public static bool IsWithinTextInput(IInputElement? element) =>
        IsWithin<TextBox>(element) || IsWithin<NumericUpDown>(element);

    public static bool TryHandleCommonShortcuts(
        Window window,
        MainWindowViewModel? mainVm,
        KeyEventArgs e,
        bool closeOnEscape)
    {
        if (closeOnEscape && e.Key == Key.Escape)
        {
            window.Close();
            e.Handled = true;
            return true;
        }

        if (IsWithinTextInput(window.FocusManager?.GetFocusedElement()))
            return false;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && mainVm is not null)
        {
            if ((e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) || e.Key == Key.Y)
            {
                if (mainVm.RedoCommand.CanExecute(null))
                    mainVm.RedoCommand.Execute(null);
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.Z)
            {
                if (mainVm.UndoCommand.CanExecute(null))
                    mainVm.UndoCommand.Execute(null);
                e.Handled = true;
                return true;
            }
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && mainVm is not null)
        {
            mainVm.TogglePlaybackCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        return false;
    }
}
