using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int MaxUndoHistory = 100;

    private readonly record struct NoteSnapshot(
        int Measure,
        string LaneId,
        double Position,
        string WavKey,
        NoteType Type,
        int BranchId,
        int SourceLineOrder)
    {
        public static NoteSnapshot From(BmsNote note) => new(
            note.Measure,
            note.LaneId,
            note.Position,
            note.WavKey,
            note.Type,
            note.BranchId,
            note.SourceLineOrder);

        public BmsNote ToNote() => new()
        {
            Measure = Measure,
            LaneId = LaneId,
            Position = Position,
            WavKey = WavKey,
            Type = Type,
            BranchId = BranchId,
            SourceLineOrder = SourceLineOrder,
        };
    }

    private sealed record NoteEditHistoryEntry(
        NoteSnapshot[] Before,
        int[] BeforeSelectedIndices,
        NoteSnapshot[] After,
        int[] AfterSelectedIndices,
        string Description);

    private readonly List<NoteEditHistoryEntry> _undoStack = new();
    private readonly List<NoteEditHistoryEntry> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (IsPlaying || _undoStack.Count == 0)
            return;

        var entry = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(entry);

        ApplyNoteSnapshot(entry.Before, entry.BeforeSelectedIndices);
        NotifyUndoRedoStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (IsPlaying || _redoStack.Count == 0)
            return;

        var entry = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(entry);

        ApplyNoteSnapshot(entry.After, entry.AfterSelectedIndices);
        NotifyUndoRedoStateChanged();
    }

    private (NoteSnapshot[] Notes, int[] SelectedIndices) CaptureNoteSnapshot()
    {
        var count = Chart.Notes.Count;
        if (count == 0)
            return (Array.Empty<NoteSnapshot>(), Array.Empty<int>());

        var snapshot = new NoteSnapshot[count];
        List<int>? selected = _selectedNotes.Count > 0 ? new List<int>(_selectedNotes.Count) : null;

        for (var i = 0; i < count; i++)
        {
            var note = Chart.Notes[i];
            snapshot[i] = NoteSnapshot.From(note);
            if (selected is not null && _selectedNotes.Contains(note))
                selected.Add(i);
        }

        return (snapshot, selected is { Count: > 0 } ? selected.ToArray() : Array.Empty<int>());
    }

    private void RecordNoteHistory(NoteSnapshot[] beforeNotes, int[] beforeSelected, string description)
    {
        var (afterNotes, afterSelected) = CaptureNoteSnapshot();
        var entry = new NoteEditHistoryEntry(beforeNotes, beforeSelected, afterNotes, afterSelected, description);

        if (_undoStack.Count >= MaxUndoHistory)
            _undoStack.RemoveAt(0);

        _undoStack.Add(entry);
        _redoStack.Clear();
        NotifyUndoRedoStateChanged();
    }

    private void ApplyNoteSnapshot(NoteSnapshot[] snapshot, int[] selectedIndices)
    {
        Chart.Notes.Clear();
        if (Chart.Notes.Capacity < snapshot.Length)
            Chart.Notes.Capacity = snapshot.Length;

        var restored = new BmsNote[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var note = snapshot[i].ToNote();
            restored[i] = note;
            Chart.Notes.Add(note);
        }

        _selectedNotes.Clear();
        foreach (var index in selectedIndices)
        {
            if ((uint)index < (uint)restored.Length)
                _selectedNotes.Add(restored[index]);
        }

        NotifyNotesChanged();
        NotifySelectionChanged();
    }

    private void ClearUndoHistory()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
            return;

        _undoStack.Clear();
        _redoStack.Clear();
        NotifyUndoRedoStateChanged();
    }

    private void NotifyUndoRedoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
