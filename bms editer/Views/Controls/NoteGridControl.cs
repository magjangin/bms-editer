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

    public static readonly StyledProperty<System.Windows.Input.ICommand?> PlaceNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(PlaceNoteCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> RemoveNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(RemoveNoteCommand));

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

    static NoteGridControl()
    {
        AffectsRender<NoteGridControl>(LanesProperty, LaneWidthProperty, NotesProperty);
        AffectsMeasure<NoteGridControl>(LanesProperty, LaneWidthProperty);
    }

    public NoteGridControl()
    {
        PointerPressed += OnPointerPressed;
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
        if (notes is not null)
        {
            for (var index = 0; index < notes.Count; index++)
            {
                var note = notes[index];
                
                var laneIndex = -1;
                for (var j = 0; j < lanes.Count; j++)
                {
                    if (lanes[j].Id == note.LaneId)
                    {
                        laneIndex = j;
                        break;
                    }
                }
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

                double ratio = 0.0;
                double noteTPos = 0.0;
                
                if (DurationSeconds > 0 && Bpm > 0)
                {
                    var secondsPerMeasure = 240.0 / Bpm;
                    var seconds = (note.Measure + note.Position) * secondsPerMeasure;
                    ratio = seconds / DurationSeconds;
                    noteTPos = IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
                }
                else
                {
                    var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
                    var totalOffset = (note.Measure + note.Position) * rowHeight;
                    noteTPos = IsHorizontalView ? totalOffset : (timelineLength - totalOffset);
                }

                var laneOffset = laneIndex * laneThickness;

                if (IsHorizontalView)
                {
                    var rect = new Rect(noteTPos - 3, laneOffset + 2, 6, laneThickness - 4);
                    context.FillRectangle(noteBrush, rect);
                    context.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
                }
                else
                {
                    var rect = new Rect(laneOffset + 2, noteTPos - 3, laneThickness - 4, 6);
                    context.FillRectangle(noteBrush, rect);
                    context.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
                }
            }
        }

        DrawGridSyncFlash(context, totalWidth, totalHeight);
        DrawPlaybackCursor(context, totalWidth, totalHeight);
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0 || Bpm <= 0)
            return;

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

    private void DrawPlaybackCursor(DrawingContext context, double width, double height)
    {
        if (!IsPlaybackCursorVisible || DurationSeconds <= 0)
            return;

        var ratio = Math.Clamp(PlaybackPositionSeconds / DurationSeconds, 0, 1);
        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 50, 255, 120)), 5);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 255, 90)), 2);

        if (IsHorizontalView)
        {
            var x = ratio * width;
            context.DrawLine(glowPen, new Point(x, 0), new Point(x, height));
            context.DrawLine(cursorPen, new Point(x, 0), new Point(x, height));
        }
        else
        {
            var y = (1.0 - ratio) * height;
            context.DrawLine(glowPen, new Point(0, y), new Point(width, y));
            context.DrawLine(cursorPen, new Point(0, y), new Point(width, y));
        }
    }

    private void DrawGridSyncFlash(DrawingContext context, double width, double height)
    {
        if (!IsGridSyncFlashVisible)
            return;

        context.FillRectangle(new SolidColorBrush(Color.FromArgb(36, 80, 255, 140)), new Rect(0, 0, width, height));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(190, 90, 255, 150)), 2), new Rect(1, 1, width - 2, height - 2));
    }

}
