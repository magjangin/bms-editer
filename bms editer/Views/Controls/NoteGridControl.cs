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

    public static readonly StyledProperty<bool> SnapToGridProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(SnapToGrid), true);

    // 끄면 클릭한 자리에 그대로 찍는다. 잇단음처럼 격자로 표현할 수 없는 자리를
    // 손으로 잡을 때 쓴다. 예전에는 이 값이 바인딩만 되어 있고 아무도 읽지 않아서,
    // 체크를 꺼도 언제나 격자에 반올림됐다.
    public bool SnapToGrid
    {
        get => GetValue(SnapToGridProperty);
        set => SetValue(SnapToGridProperty, value);
    }

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

    // 노트 한 개마다 새로 만들면 프레임당 수천 개가 할당된다. 색이 고정이라 나눠 쓴다.
    private static readonly Pen NoteOutlinePen = new(Brushes.Black, 1);

    private static readonly IBrush ScratchNoteBrush = new SolidColorBrush(Color.FromRgb(230, 40, 40));
    private static readonly IBrush BlackKeyNoteBrush = new SolidColorBrush(Color.FromRgb(40, 140, 230));
    private static readonly IBrush WhiteKeyNoteBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));

    // 노트 채움색도 세 가지뿐이라 나눠 쓴다. 예전에는 노트마다 새로 만들었다.
    private static IBrush GetNoteBrush(string laneId) => laneId switch
    {
        "16" => ScratchNoteBrush,                          // 스크래치는 빨강
        "12" or "14" or "18" => BlackKeyNoteBrush,         // 흑건은 파랑
        _ => WhiteKeyNoteBrush,                            // 백건은 백색
    };

    // 격자 펜과 배경도 매 프레임 새로 만들 이유가 없다.
    private static readonly IBrush GridBackgroundBrush = new SolidColorBrush(Color.FromArgb(40, 30, 60, 120));
    private static readonly IPen LanePen = new Pen(Brushes.DimGray, 1);
    private static readonly IPen SubBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 150, 160, 170)), 1);
    private static readonly IPen BeatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 190, 200, 210)), 1);
    private static readonly IPen MeasurePen = new Pen(Brushes.White, 1.5);
    private static readonly IBrush LaneHeaderBrush = new SolidColorBrush(Color.FromArgb(140, 200, 200, 200));
    private static readonly IBrush DragFillBrush = new SolidColorBrush(Color.FromArgb(60, 255, 220, 60));
    private static readonly IPen DragOutlinePen = new Pen(Brushes.Yellow, 1);
    private static readonly Typeface LaneHeaderTypeface = new("Inter, Arial, sans-serif");

    // 선택한 노트를 둘러 그리는 펜.
    private static readonly Pen SelectionPen = new(Brushes.Yellow, 2);

    static NoteGridControl()
    {
        AffectsRender<NoteGridControl>(LanesProperty, LaneWidthProperty, NotesProperty, IsCircleNoteShapeProperty,
            SelectedNotesProperty);
        AffectsMeasure<NoteGridControl>(LanesProperty, LaneWidthProperty);
    }

    private Point? _dragStartPoint;
    private Point? _dragCurrentPoint;

    // 이번 드래그가 기존 선택에 더하는 것인지(Ctrl/Shift), 갈아끼우는 것인지.
    private bool _isAdditiveDrag;

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
        context.FillRectangle(GridBackgroundBrush, new Rect(0, 0, totalWidth, totalHeight));


        var thicknessOffset = 0.0;
        for (var i = 0; i <= lanes.Count; i++)
        {
            if (IsHorizontalView)
            {
                context.DrawLine(LanePen, new Point(0, thicknessOffset), new Point(totalWidth, thicknessOffset));
            }
            else
            {
                context.DrawLine(LanePen, new Point(thicknessOffset, 0), new Point(thicknessOffset, totalHeight));
            }

            if (i < lanes.Count)
                thicknessOffset += laneThickness;
        }

        foreach (var line in EnumerateGridLines(timelineLength))
        {
            var pen = line.Kind switch
            {
                GridLineKind.Measure => MeasurePen,
                GridLineKind.Beat => BeatPen,
                _ => SubBeatPen,
            };

            if (IsHorizontalView)
            {
                context.DrawLine(pen, new Point(line.Position, 0), new Point(line.Position, totalHeight));
            }
            else
            {
                context.DrawLine(pen, new Point(0, line.Position), new Point(totalWidth, line.Position));
            }
        }

        // 래인 번호(채널 번호) 텍스트 그리기
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var formattedText = new FormattedText(
                lane.Header,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LaneHeaderTypeface,
                12.0,
                LaneHeaderBrush);

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

                var noteTPos = ComputeNoteTPos(note, timelineLength);
                var noteBrush = GetNoteBrush(note.LaneId);
                var laneOffset = laneIndex * laneThickness;
                var blackPen = NoteOutlinePen;

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
                    var highlightPen = SelectionPen;
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
            context.FillRectangle(DragFillBrush, selectionRect);
            context.DrawRectangle(null, DragOutlinePen, selectionRect);
        }

        DrawGridSyncFlash(context, totalWidth, totalHeight);
        DrawPlaybackCursor(context, totalWidth, totalHeight);
    }

    private double ComputeNoteTPos(BmsNote note, double timelineLength)
    {
        if (DurationSeconds > 0 && Bpm > 0)
        {
            // 노트의 시각도 격자와 같은 곳에서 구해야 둘이 어긋나지 않는다.
            var seconds = EffectiveTimeline.SecondsAt(note.Measure + note.Position);
            return ToTimelinePosition(seconds / DurationSeconds, timelineLength);
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

        // Ctrl/Shift 를 누른 채 끌면 **편집 모드에서도** 범위 선택이 된다.
        //
        // 예전에는 모드로만 갈려서, 찍고 -> 고르고 -> 방향키로 옮기려면 매번 ✏️ 토글을
        // 왕복해야 했다. 게다가 토글을 누르는 순간 포커스가 격자를 떠나 방향키도 안 먹었다.
        var additive = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        if (!IsEditMode || additive)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                _dragStartPoint = point.Position;
                _dragCurrentPoint = point.Position;
                _isAdditiveDrag = additive;
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

        var ratio = Math.Clamp(IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength)), 0.0, 1.0);

        // 클릭한 자리를 마디 위치로 되돌린다. 그리는 쪽과 같은 시간축을 쓴다.
        var clickedMeasurePosition = DurationSeconds > 0
            ? EffectiveTimeline.MeasurePositionAt(ratio * DurationSeconds)
            : ratio * MeasureCount;

        if (SnapToGrid)
        {
            var totalStepIndex = (int)Math.Round(clickedMeasurePosition * split);

            // 맨 끝을 클릭하면 반올림이 마디 경계를 딱 넘어서 measure == MeasureCount 가 된다.
            // 예전에는 그대로 거부해서 **곡 마지막 격자 칸에는 노트를 찍을 수 없었다.**
            // 거부하는 대신 마지막 칸으로 당겨준다.
            totalStepIndex = Math.Clamp(totalStepIndex, 0, (MeasureCount * split) - 1);

            measure = totalStepIndex / split;
            position = (double)(totalStepIndex % split) / split;
        }
        else
        {
            var clamped = Math.Clamp(clickedMeasurePosition, 0, MeasureCount - (1.0 / split));
            measure = (int)Math.Floor(clamped);
            position = clamped - measure;
        }

        if (measure < 0 || measure >= MeasureCount)
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

        var args = new NoteSelectionArgs(selected, _isAdditiveDrag);
        _isAdditiveDrag = false;

        if (SelectNotesCommand?.CanExecute(args) == true)
        {
            SelectNotesCommand.Execute(args);
        }

        InvalidateVisual();
    }

}
