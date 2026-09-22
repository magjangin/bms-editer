using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services;

// 조건 블록(#RANDOM · #IF · #SWITCH) 문법을 읽는 한 곳.
//
// 파서는 "이 줄이 어느 갈래에 속하는가"(BranchId)를, 라이터는 "어디부터 어디까지가
// 조건 블록인가"(영역)를 알아야 한다. 둘이 제어 줄을 따로 해석하면 한쪽만 고치게 되고,
// 실제로 그렇게 어긋나 있었다.
//   * 파서는 #CASE·#DEF 가 나올 때마다 갈래를 쌓기만 하고 닫지 않아서,
//     #ENDSW 뒤의 평범한 노트가 마지막 CASE 의 갈래 번호를 달고 읽혔다.
//   * 라이터는 제어 줄을 "바로 앞 데이터 줄의 마디" 에 매달아 마디 순으로 다시 정렬해서,
//     블록 뒤의 무조건 줄이 #IF 안으로 끌려 들어가거나 #IF 가 닫히지 않은 파일이 나왔다.
// 그래서 두 쪽이 같은 상태 기계(Tracker)를 쓴다.
internal static partial class ConditionalBlocks
{
    public enum Kind
    {
        None,
        Random,
        If,
        Else,
        EndIf,
        EndRandom,
        Switch,
        Case,
        Skip,
        EndSwitch,
    }

    [GeneratedRegex(@"^#([A-Za-z]+)(?:\s|$)")]
    private static partial Regex CommandRegex();

    public static Kind Classify(string line)
    {
        var match = CommandRegex().Match(line.Trim());
        if (!match.Success)
            return Kind.None;

        return match.Groups[1].Value.ToUpperInvariant() switch
        {
            "RANDOM" or "SETRANDOM" or "RONDAM" => Kind.Random,
            "IF" => Kind.If,
            "ELSEIF" or "ELSE" => Kind.Else,
            "ENDIF" => Kind.EndIf,
            "ENDRANDOM" => Kind.EndRandom,
            "SWITCH" or "SETSWITCH" => Kind.Switch,
            "CASE" or "DEF" => Kind.Case,
            "SKIP" => Kind.Skip,
            "ENDSW" => Kind.EndSwitch,
            _ => Kind.None,
        };
    }

    // 제어 줄을 차례로 먹이면 지금 어느 갈래 안인지 알려준다.
    //
    // #RANDOM·#SWITCH 는 "블록"을 열 뿐 갈래를 정하지 않는다. 갈래는 #IF·#ELSE·#CASE·#DEF 가 정한다.
    // #ENDRANDOM 은 규격상 생략되는 일이 흔해서, 없으면 블록이 파일 끝까지 이어진 것으로 본다.
    public sealed class Tracker
    {
        private enum FrameKind { Random, If, Switch }

        private sealed class Frame(FrameKind kind, int branchId)
        {
            public FrameKind Kind { get; } = kind;
            public int BranchId { get; set; } = branchId;
        }

        private readonly Stack<Frame> _frames = new();
        private int _nextBranchId = 1;

        public bool IsInsideBlock => _frames.Count > 0;

        // 가장 안쪽 갈래의 번호. 어느 갈래에도 들지 않았으면 0.
        public int CurrentBranchId
        {
            get
            {
                foreach (var frame in _frames)
                {
                    if (frame.BranchId > 0)
                        return frame.BranchId;
                }

                return 0;
            }
        }

        // 제어 줄이면 상태를 바꾸고 true.
        public bool Apply(string line)
        {
            var kind = Classify(line);
            switch (kind)
            {
                case Kind.None:
                    return false;

                case Kind.Random:
                    _frames.Push(new Frame(FrameKind.Random, 0));
                    break;

                case Kind.If:
                    _frames.Push(new Frame(FrameKind.If, _nextBranchId++));
                    break;

                case Kind.Else:
                    if (_frames.TryPeek(out var ifFrame) && ifFrame.Kind == FrameKind.If)
                        ifFrame.BranchId = _nextBranchId++;
                    break;

                case Kind.EndIf:
                    if (_frames.TryPeek(out var top) && top.Kind == FrameKind.If)
                        _frames.Pop();
                    break;

                case Kind.EndRandom:
                    PopThrough(FrameKind.Random);
                    break;

                case Kind.Switch:
                    _frames.Push(new Frame(FrameKind.Switch, 0));
                    break;

                case Kind.Case:
                    // CASE 는 갈래를 새로 쌓지 않는다. 같은 SWITCH 의 다음 갈래로 넘어갈 뿐이다.
                    if (_frames.FirstOrDefault(f => f.Kind == FrameKind.Switch) is { } switchFrame)
                    {
                        while (_frames.Peek() != switchFrame)
                            _frames.Pop();

                        switchFrame.BranchId = _nextBranchId++;
                    }
                    break;

                case Kind.Skip:
                    break;

                case Kind.EndSwitch:
                    PopThrough(FrameKind.Switch);
                    break;
            }

            return true;
        }

        // 그 종류의 블록이 열려 있을 때만, 그 블록까지 닫는다. 안쪽에 닫히지 않은 #IF 가 있어도 함께 닫는다.
        private void PopThrough(FrameKind kind)
        {
            if (!_frames.Any(f => f.Kind == kind))
                return;

            while (_frames.Pop().Kind != kind)
            {
            }
        }
    }

    // 원문 줄 순서(Order)로 본 조건 블록 영역들. [Start, End] 모두 포함.
    //
    // 영역 안의 줄은 저장할 때 원래 순서 그대로 내보낸다. 조건 블록의 뜻은 줄의 **위치**에 달려 있어서,
    // 마디 순으로 다시 정렬하면 줄이 갈래 안팎을 넘나든다.
    // 짝 없이 떨어진 제어 줄(#ENDIF 만 있는 등)은 그 줄 하나짜리 영역이 되어 제자리를 지킨다.
    public static IReadOnlyList<(int Start, int End)> FindRegions(IEnumerable<BmsRawLine> lines)
    {
        var regions = new List<(int Start, int End)>();
        var tracker = new Tracker();
        var start = 0;

        foreach (var line in lines.Where(l => l.IsControlFlow).OrderBy(l => l.Order))
        {
            var wasInside = tracker.IsInsideBlock;
            tracker.Apply(line.Text);

            if (!wasInside)
                start = line.Order;

            if (!tracker.IsInsideBlock)
                regions.Add((start, line.Order));
        }

        if (tracker.IsInsideBlock)
            regions.Add((start, int.MaxValue));

        return regions;
    }

    public static bool Contains(IReadOnlyList<(int Start, int End)> regions, int order)
    {
        foreach (var (start, end) in regions)
        {
            if (order >= start && order <= end)
                return true;
        }

        return false;
    }
}
