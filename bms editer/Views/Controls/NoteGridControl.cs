using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using bms_editer.Models;

namespace bms_editer.Views.Controls;

// 마디/레인 그리드를 직접 그리는 커스텀 컨트롤.
// 노트 배치·선택·드래그 편집은 다음 단계에서 확장.
public sealed class NoteGridControl : TimelineControlBase
{
    public static readonly StyledProperty<IReadOnlyList<LaneDefinition>?> LanesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<LaneDefinition>?>(nameof(Lanes));

    public static readonly StyledProperty<double> LaneWidthProperty =
        AvaloniaProperty.Register<NoteGridControl, double>(nameof(LaneWidth), 40.0);

    public static readonly StyledProperty<IReadOnlyList<BmsNote>?> NotesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<BmsNote>?>(nameof(Notes));

    public static readonly StyledProperty<bool> IsCircleNoteShapeProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(IsCircleNoteShape));

    public static readonly StyledProperty<bool> IsEditModeProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(IsEditMode));

    public static readonly StyledProperty<IReadOnlyList<BmsNote>?> SelectedNotesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<BmsNote>?>(nameof(SelectedNotes));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> PlaceNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(PlaceNoteCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> RemoveNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(RemoveNoteCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> SelectNotesCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(SelectNotesCommand));

    public IReadOnlyList<LaneDefinition>? Lanes
    {
        get => GetValue(LanesProperty);
        set => SetValue(LanesProperty, value);
    }

    public double LaneWidth
    {
        get => GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    public IReadOnlyList<BmsNote>? Notes
    {
        get => GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public bool IsCircleNoteShape
    {
        get => GetValue(IsCircleNoteShapeProperty);
        set => SetValue(IsCircleNoteShapeProperty, value);
    }

    public bool IsEditMode
    {
        get => GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    public IReadOnlyList<BmsNote>? SelectedNotes
    {
        get => GetValue(SelectedNotesProperty);
        set => SetValue(SelectedNotesProperty, value);
    }

    public System.Windows.Input.ICommand? PlaceNoteCommand
    {
        get => GetValue(PlaceNoteCommandProperty);
        set => SetValue(PlaceNoteCommandProperty, value);
    }

    public System.Windows.Input.ICommand? RemoveNoteCommand
    {
        get => GetValue(RemoveNoteCommandProperty);
        set => SetValue(RemoveNoteCommandProperty, value);
    }

    public System.Windows.Input.ICommand? SelectNotesCommand
    {
        get => GetValue(SelectNotesCommandProperty);
        set => SetValue(SelectNotesCommandProperty, value);
    }

    static NoteGridControl()
    {
        AffectsRender<NoteGridControl>(LanesProperty, LaneWidthProperty, NotesProperty, IsCircleNoteShapeProperty, SelectedNotesProperty);
        AffectsMeasure<NoteGridControl>(LanesProperty, LaneWidthProperty);
    }

    private Point? _dragStartPoint;
    private Point? _dragCurrentPoint;

    public NoteGridControl()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMovedForSelection;
        PointerReleased += OnPointerReleased;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var laneCount = Lanes?.Count ?? 0;
        var timelineLength = GetTimelineHeight();
        var lanesTotalThickness = laneCount * LaneWidth * HorizontalZoom;

        if (IsHorizontalView)
        {
            return new Size(timelineLength, lanesTotalThickness);
        }
        else
        {
            return new Size(lanesTotalThickness, timelineLength);
        }
    }

    public override void Render(DrawingContext context)
    {
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0)
            return;

        var laneThickness = LaneWidth * HorizontalZoom;
        var totalLanesThickness = lanes.Count * laneThickness;
        var timelineLength = GetTimelineHeight();

        var totalWidth = IsHorizontalView ? timelineLength : totalLanesThickness;
        var totalHeight = IsHorizontalView ? totalLanesThickness : timelineLength;

        context.FillRectangle(Brushes.Black, new Rect(0, 0, totalWidth, totalHeight));
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 30, 60, 120)), new Rect(0, 0, totalWidth, totalHeight));

        var lanePen = new Pen(Brushes.DimGray, 1);
        var subBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 150, 160, 170)), 1);
        var beatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 190, 200, 210)), 1);
        var measurePen = new Pen(Brushes.White, 1.5);

        var thicknessOffset = 0.0;
        for (var i = 0; i <= lanes.Count; i++)
        {
            if (IsHorizontalView)
            {
                context.DrawLine(lanePen, new Point(0, thicknessOffset), new Point(totalWidth, thicknessOffset));
            }
            else
            {
                context.DrawLine(lanePen, new Point(thicknessOffset, 0), new Point(thicknessOffset, totalHeight));
            }

            if (i < lanes.Count)
                thicknessOffset += laneThickness;
        }

        var split = Math.Max(1, BeatSplit);
        if (DurationSeconds > 0 && Bpm > 0)
        {
            var secondsPerStep = 240.0 / (Bpm * split);
            for (var index = 0; ; index++)
            {
                var seconds = index * secondsPerStep;
                if (seconds > DurationSeconds)
                    goto FinishedBeatLines;

                var ratio = seconds / DurationSeconds;
                var tPos = IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
                var pen = Mod(index, split) == 0
                    ? measurePen
                    : IsMeasureBeatLine(index, split, GridMeasure)
                        ? beatPen
                        : subBeatPen;

                if (IsHorizontalView)
                {
                    context.DrawLine(pen, new Point(tPos, 0), new Point(tPos, totalHeight));
                }
                else
                {
                    context.DrawLine(pen, new Point(0, tPos), new Point(totalWidth, tPos));
                }
            }
        }
        else
        {
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var tPos = IsHorizontalView ? 0.0 : timelineLength;
            for (var measure = 0; measure < MeasureCount; measure++)
            {
                for (var beat = 0; beat < split; beat++)
                {
                    var beatTPos = IsHorizontalView 
                        ? (tPos + (rowHeight * beat / split))
                        : (tPos - (rowHeight * beat / split));

                    var pen = beat == 0
                        ? measurePen
                        : IsMeasureBeatLine(beat, split, GridMeasure)
                            ? beatPen
                            : subBeatPen;

                    if (IsHorizontalView)
                    {
                        context.DrawLine(pen, new Point(beatTPos, 0), new Point(beatTPos, totalHeight));
                    }
                    else
                    {
                        context.DrawLine(pen, new Point(0, beatTPos), new Point(totalWidth, beatTPos));
                    }
                }

                if (IsHorizontalView)
                    tPos += rowHeight;
                else
                    tPos -= rowHeight;
            }
        }

    FinishedBeatLines:
        // 래인 번호(채널 번호) 텍스트 그리기
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var formattedText = new FormattedText(
                lane.Header,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter, Arial, sans-serif"),
                12.0,
                new SolidColorBrush(Color.FromArgb(140, 200, 200, 200)));

            if (IsHorizontalView)
            {
                var laneY = (i * laneThickness) + (laneThickness - formattedText.Height) / 2;
                context.DrawText(formattedText, new Point(8, laneY));
            }
            else
            {
                var laneX = (i * laneThickness) + (laneThickness - formattedText.Width) / 2;
                context.DrawText(formattedText, new Point(laneX, totalHeight - formattedText.Height - 8));
            }
        }

        // 배치된 노트 그리기
        var notes = Notes;
        var selectedSet = SelectedNotes is null ? null : new HashSet<BmsNote>(SelectedNotes);
        if (notes is not null)
        {
            for (var index = 0; index < notes.Count; index++)
            {
                var note = notes[index];

                var laneIndex = FindLaneIndex(lanes, note.LaneId);
                if (laneIndex == -1) continue;

                IBrush noteBrush = Brushes.White;
                if (note.LaneId == "16")
                {
                    noteBrush = new SolidColorBrush(Color.FromRgb(230, 40, 40)); // 스크래치는 빨강
                }
                else if (note.LaneId == "12" || note.LaneId == "14" || note.LaneId == "18")
                {
                    noteBrush = new SolidColorBrush(Color.FromRgb(40, 140, 230)); // 흑건은 파랑
                }
                else
                {
                    noteBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 백건은 백색
                }

                var noteTPos = ComputeNoteTPos(note, timelineLength);
                var laneOffset = laneIndex * laneThickness;
                var blackPen = new Pen(Brushes.Black, 1);

                if (IsHorizontalView)
                {
                    if (IsCircleNoteShape)
                    {
                        var radius = Math.Min(7.5, (laneThickness - 4) / 2);
                        var center = new Point(noteTPos, laneOffset + laneThickness / 2);
                        context.DrawEllipse(noteBrush, blackPen, center, radius, radius);
                    }
                    else
                    {
                        var rect = new Rect(noteTPos - 3, laneOffset + 2, 6, laneThickness - 4);
                        context.FillRectangle(noteBrush, rect);
                        context.DrawRectangle(null, blackPen, rect);
                    }
                }
                else
                {
                    if (IsCircleNoteShape)
                    {
                        var radius = Math.Min(7.5, (laneThickness - 4) / 2);
                        var center = new Point(laneOffset + laneThickness / 2, noteTPos);
                        context.DrawEllipse(noteBrush, blackPen, center, radius, radius);
                    }
                    else
                    {
                        var rect = new Rect(laneOffset + 2, noteTPos - 3, laneThickness - 4, 6);
                        context.FillRectangle(noteBrush, rect);
                        context.DrawRectangle(null, blackPen, rect);
                    }
                }

                if (selectedSet is not null && selectedSet.Contains(note))
                {
                    var highlightPen = new Pen(Brushes.Yellow, 2);
                    var highlightRect = IsHorizontalView
                        ? new Rect(noteTPos - 7, laneOffset + 1, 14, laneThickness - 2)
                        : new Rect(laneOffset + 1, noteTPos - 7, laneThickness - 2, 14);
                    context.DrawRectangle(null, highlightPen, highlightRect);
                }
            }
        }

        if (_dragStartPoint is { } dragStart && _dragCurrentPoint is { } dragEnd)
        {
            var selectionRect = NormalizedRect(dragStart, dragEnd);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 220, 60)), selectionRect);
            context.DrawRectangle(null, new Pen(Brushes.Yellow, 1), selectionRect);
        }

        DrawGridSyncFlash(context, totalWidth, totalHeight);
        DrawPlaybackCursor(context, totalWidth, totalHeight);
    }

    private double ComputeNoteTPos(BmsNote note, double timelineLength)
    {
        if (DurationSeconds > 0 && Bpm > 0)
        {
            var secondsPerMeasure = 240.0 / Bpm;
            var seconds = (note.Measure + note.Position) * secondsPerMeasure;
            var ratio = seconds / DurationSeconds;
            return IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
        }

        var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
        var totalOffset = (note.Measure + note.Position) * rowHeight;
        return IsHorizontalView ? totalOffset : (timelineLength - totalOffset);
    }

    private static int FindLaneIndex(IReadOnlyList<LaneDefinition> lanes, string laneId)
    {
        for (var j = 0; j < lanes.Count; j++)
        {
            if (lanes[j].Id == laneId)
                return j;
        }
        return -1;
    }

    private static Rect NormalizedRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        Focus();

        var point = e.GetCurrentPoint(this);
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0 || Bpm <= 0)
            return;

        if (!IsEditMode)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                _dragStartPoint = point.Position;
                _dragCurrentPoint = point.Position;
                e.Pointer.Capture(this);
                InvalidateVisual();
            }
            return;
        }

        var laneThickness = LaneWidth * HorizontalZoom;
        var timelineLength = GetTimelineHeight();

        var thicknessPos = IsHorizontalView ? point.Position.Y : point.Position.X;
        var clickedLaneIndex = (int)(thicknessPos / laneThickness);
        if (clickedLaneIndex < 0 || clickedLaneIndex >= lanes.Count)
            return;

        var clickedLaneId = lanes[clickedLaneIndex].Id;

        var tPos = IsHorizontalView ? point.Position.X : point.Position.Y;
        var split = Math.Max(1, BeatSplit);

        int measure = 0;
        double position = 0.0;

        if (DurationSeconds > 0)
        {
            var ratio = IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength));
            ratio = Math.Clamp(ratio, 0.0, 1.0);

            var seconds = ratio * DurationSeconds;
            var secondsPerMeasure = 240.0 / Bpm;
            var secondsPerStep = secondsPerMeasure / split;

            var totalStepIndex = (int)Math.Round(seconds / secondsPerStep);
            measure = totalStepIndex / split;
            position = (double)(totalStepIndex % split) / split;
        }
        else
        {
            var ratio = IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength));
            ratio = Math.Clamp(ratio, 0.0, 1.0);

            var totalSteps = MeasureCount * split;
            var totalStepIndex = (int)Math.Round(ratio * totalSteps);

            measure = totalStepIndex / split;
            position = (double)(totalStepIndex % split) / split;
        }

        if (measure < 0 || measure >= MeasureCount || position < 0 || position >= 1.0)
            return;

        var args = new NotePlacementArgs(clickedLaneId, measure, position);

        if (point.Properties.IsLeftButtonPressed)
        {
            if (PlaceNoteCommand?.CanExecute(args) == true)
            {
                PlaceNoteCommand.Execute(args);
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            if (RemoveNoteCommand?.CanExecute(args) == true)
            {
                RemoveNoteCommand.Execute(args);
            }
        }
    }

    private void OnPointerMovedForSelection(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_dragStartPoint is null)
            return;

        _dragCurrentPoint = e.GetCurrentPoint(this).Position;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_dragStartPoint is not { } dragStart || _dragCurrentPoint is not { } dragEnd)
            return;

        _dragStartPoint = null;
        _dragCurrentPoint = null;
        e.Pointer.Capture(null);

        var lanes = Lanes;
        var notes = Notes;
        if (lanes is null || lanes.Count == 0 || notes is null)
        {
            InvalidateVisual();
            return;
        }

        var laneThickness = LaneWidth * HorizontalZoom;
        var timelineLength = GetTimelineHeight();
        var selectionRect = NormalizedRect(dragStart, dragEnd).Inflate(2);

        var selected = new List<BmsNote>();
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var laneIndex = FindLaneIndex(lanes, note.LaneId);
            if (laneIndex == -1) continue;

            var noteTPos = ComputeNoteTPos(note, timelineLength);
            var laneOffset = laneIndex * laneThickness;
            var noteCenter = IsHorizontalView
                ? new Point(noteTPos, laneOffset + laneThickness / 2)
                : new Point(laneOffset + laneThickness / 2, noteTPos);

            if (selectionRect.Contains(noteCenter))
                selected.Add(note);
        }

        var args = new NoteSelectionArgs(selected);
        if (SelectNotesCommand?.CanExecute(args) == true)
        {
            SelectNotesCommand.Execute(args);
        }

        InvalidateVisual();
    }

}
