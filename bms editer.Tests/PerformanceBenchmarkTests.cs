using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using bms_editer.Views.Controls;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

public sealed class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output) => _output = output;

    private static (BmsChart Chart, List<HoldLink> Links, List<float> Peaks, List<float> Onsets) Create5MinuteChart()
    {
        const int measures = 190; // BPM 150 기준 약 5분 (304초)
        const int split = 16;
        var lanes = new[]
        {
            new LaneDefinition("16", "SC"),
            new LaneDefinition("11", "1"),
            new LaneDefinition("12", "2"),
            new LaneDefinition("13", "3"),
            new LaneDefinition("14", "4"),
            new LaneDefinition("15", "5"),
            new LaneDefinition("18", "6"),
        };

        var notes = new List<BmsNote>();
        var links = new List<HoldLink>();

        // 5분 동안 약 3,000개 노트 + 200개 홀드
        var rnd = new Random(42);
        for (var m = 0; m < measures; m++)
        {
            for (var b = 0; b < split; b++)
            {
                if (rnd.NextDouble() < 0.35)
                {
                    var lane = lanes[rnd.Next(lanes.Length)].Id;
                    notes.Add(new BmsNote
                    {
                        LaneId = lane,
                        Measure = m,
                        Position = b / (double)split,
                        WavKey = "01"
                    });
                }
            }

            if (m % 3 == 0)
            {
                var lane = lanes[rnd.Next(lanes.Length)].Id;
                var head = new BmsNote { LaneId = lane, Measure = m, Position = 0.0, WavKey = "02" };
                var tail = new BmsNote { LaneId = lane, Measure = m + 1, Position = 0.5, WavKey = "03" };
                notes.Add(head);
                notes.Add(tail);
                links.Add(new HoldLink(head, tail, "NORMAL"));
            }
        }

        var chart = new BmsChart
        {
            Lanes = lanes,
            MeasureCount = measures,
        };
        chart.Header.Bpm = 150;
        chart.Header.Title = "5분 벤치마크";
        foreach (var note in notes)
            chart.Notes.Add(note);

        var peaks = new List<float>(20000);
        var onsets = new List<float>(20000);
        for (var i = 0; i < 20000; i++)
        {
            peaks.Add((float)(rnd.NextDouble() * 0.8 + 0.1));
            onsets.Add((float)(rnd.NextDouble() < 0.05 ? rnd.NextDouble() : 0.0));
        }

        return (chart, links, peaks, onsets);
    }

    [Fact]
    public void 대형_5분_차트_렌더링_성능_측정() => HoldTestSupport.RunOnUiThread(() =>
    {
        var (chart, links, peaks, onsets) = Create5MinuteChart();

        var grid = new NoteGridControl
        {
            Lanes = chart.Lanes,
            Notes = chart.Notes.ToArray(),
            HoldLinks = links,
            MeasureCount = chart.MeasureCount,
            RowHeight = 16,
            VerticalZoom = 1.0,
            HorizontalZoom = 1.0,
            LaneWidth = 40,
            BeatSplit = 16,
            GridMeasure = 4,
            Bpm = 150,
            DurationSeconds = 304,
        };

        var waveform = new OggWaveformControl
        {
            MeasureCount = chart.MeasureCount,
            RowHeight = 16,
            VerticalZoom = 1.0,
            HorizontalZoom = 1.0,
            BeatSplit = 16,
            GridMeasure = 4,
            Bpm = 150,
            DurationSeconds = 304,
            Peaks = peaks,
            Onsets = onsets,
            Width = 220,
        };

        // 워밍업 1회 렌더
        var snapshot = GridSnapshot.Capture(grid);
        var sw = Stopwatch.StartNew();

        const int iterations = 50;
        for (var i = 0; i < iterations; i++)
        {
            GridSnapshot.Capture(grid);
        }
        sw.Stop();
        var gridMsPerFrame = sw.Elapsed.TotalMilliseconds / iterations;

        sw.Restart();
        for (var i = 0; i < iterations; i++)
        {
            GridSnapshot.Capture(waveform);
        }
        sw.Stop();
        var waveformMsPerFrame = sw.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine($"=== 5분 차트 (노트 {chart.Notes.Count}개, 마디 {chart.MeasureCount}개) 렌더링 벤치마크 ===");
        _output.WriteLine($"NoteGridControl 1프레임 렌더링: {gridMsPerFrame:F2} ms");
        _output.WriteLine($"OggWaveformControl 1프레임 렌더링: {waveformMsPerFrame:F2} ms");
        _output.WriteLine($"총 1프레임 렌더 시간: {gridMsPerFrame + waveformMsPerFrame:F2} ms (30fps 예산 33.3ms 기준 점유율: {(gridMsPerFrame + waveformMsPerFrame) / 33.3 * 100:F1}%)");
    });
}
