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

    // 재생 중에는 Undo()/Redo() 가 아무것도 하지 않으므로 메뉴·단축키도 꺼 둔다.
    public bool CanUndo => !IsPlaying && _undoStack.Count > 0;
    public bool CanRedo => !IsPlaying && _redoStack.Count > 0;

    partial void OnIsPlayingChanged(bool value) => NotifyUndoRedoStateChanged();

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

        // 연달아 편집하면 이번 "편집 전"은 바로 앞 기록의 "편집 후"와 같다(되돌린 뒤라면 그 앞 기록의 "편집 후").
        // 같은 내용이면 앞 기록의 배열을 그대로 써서 단계마다 곡 전체를 두 벌씩 들고 있지 않는다.
        if (_undoStack.Count > 0 && SameNotes(_undoStack[^1].After, beforeNotes))
            beforeNotes = _undoStack[^1].After;

        var entry = new NoteEditHistoryEntry(beforeNotes, beforeSelected, afterNotes, afterSelected, description);

        if (_undoStack.Count >= MaxUndoHistory)
            _undoStack.RemoveAt(0);

        _undoStack.Add(entry);
        _redoStack.Clear();
        NotifyUndoRedoStateChanged();
    }

    private static bool SameNotes(NoteSnapshot[] a, NoteSnapshot[] b) =>
        ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);

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
