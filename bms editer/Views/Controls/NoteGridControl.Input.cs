using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using bms_editer.Models;

namespace bms_editer.Views.Controls;

public sealed partial class NoteGridControl
{
    private Point? _dragStartPoint;
    private Point? _dragCurrentPoint;

    // 이번 드래그가 기존 선택에 더하는 것인지(Ctrl/Shift), 갈아끼우는 것인지.
    private bool _isAdditiveDrag;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
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
        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

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

        if (PlacementAt(point.Position) is not { } args)
            return;

        if (point.Properties.IsLeftButtonPressed)
        {
            if (PlaceNoteCommand?.CanExecute(args) == true)
            {
                PlaceNoteCommand.Execute(args);
                InvalidateVisual();
                e.Handled = true;
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            if (RemoveNoteCommand?.CanExecute(args) == true)
            {
                RemoveNoteCommand.Execute(args);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    // 격자 위의 한 점이 어느 레인·마디·위치인지. 찍을 수 없는 자리면 null.
    public NotePlacementArgs? PlacementAt(Point point)
    {
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0)
            return null;

        var laneThickness = LaneWidth * HorizontalZoom;
        var timelineLength = GetTimelineHeight();

        var thicknessPos = IsHorizontalView ? point.Y : point.X;
        var clickedLaneIndex = (int)(thicknessPos / laneThickness);
        if (clickedLaneIndex < 0 || clickedLaneIndex >= lanes.Count)
            return null;

        var clickedLaneId = lanes[clickedLaneIndex].Id;

        var tPos = IsHorizontalView ? point.X : point.Y;
        var split = Math.Max(1, BeatSplit);

        int measure;
        double position;

        var ratio = Math.Clamp(IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength)), 0.0, 1.0);

        // 클릭한 자리를 마디 위치로 되돌린다. 그리는 쪽과 같은 시간축을 쓴다.
        var clickedMeasurePosition = DurationSeconds > 0
            ? EffectiveTimeline.MeasurePositionAt(ratio * DurationSeconds)
            : ratio * MeasureCount;

        // 마디 수 밖을 누르면 찍지 않는다.
        //
        // 예전에는 아래 당기기가 먼 곳까지 끌어와서, 음원이 마디 수보다 긴 차트에서 곡 뒤쪽을 누르면
        // 누른 자리가 아니라 마지막 마디 끝 칸에 노트가 생겼다. 화면 밖이라 찍힌 줄도 몰랐다.
        if (clickedMeasurePosition - MeasureCount > 1e-6)
            return null;

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
            return null;

        return new NotePlacementArgs(clickedLaneId, measure, position);
    }

    private void OnPointerMovedForSelection(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint is null)
            return;

        _dragCurrentPoint = e.GetCurrentPoint(this).Position;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
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

        // 그리기 색인을 그대로 쓴다. 노트 위치·레인이 화면에 그린 것과 한 치도 다를 수 없고,
        // 사각형에 걸치는 구간만 이진 탐색으로 훑는다.
        // 예전에는 놓을 때마다 곡 전체 노트의 시각을 다시 계산하고 레인을 문자열로 찾았다.
        var noteIndex = GetNoteRenderIndex(notes, lanes);
        var axis = CreateNoteAxis(timelineLength);
        var (rangeLow, rangeHigh) = IsHorizontalView
            ? (selectionRect.Left, selectionRect.Right)
            : (selectionRect.Top, selectionRect.Bottom);

        var selected = new List<BmsNote>();
        for (var index = noteIndex.FirstVisible(axis, rangeLow, rangeHigh); index < noteIndex.Count; index++)
        {
            var noteTPos = axis.Position(noteIndex.MeasurePositions[index], noteIndex.Seconds[index]);
            if (IsHorizontalView ? noteTPos > rangeHigh : noteTPos < rangeLow)
                break;

            var laneOffset = noteIndex.LaneIndices[index] * laneThickness;
            var noteCenter = IsHorizontalView
                ? new Point(noteTPos, laneOffset + laneThickness / 2)
                : new Point(laneOffset + laneThickness / 2, noteTPos);

            if (selectionRect.Contains(noteCenter))
                selected.Add(noteIndex.Notes[index]);
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
