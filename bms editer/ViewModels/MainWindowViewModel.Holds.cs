using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using bms_editer.Models;
using bms_editer.Services.Holds;

namespace bms_editer.ViewModels;

// 지금 문서의 게임 프로파일이 어디서 왔는지.
public enum HoldProfileOrigin
{
    None,

    // 파일의 #BMSEDITER_PROFILE.
    Header,

    // 헤더에 적혀 있지만 이 에디터가 모르는 id. 헤더는 그대로 두고 짝은 읽지 않는다.
    UnknownHeader,

    // 파일 경로(게임 폴더 이름)로 추정했다. 파일에는 적지 않는다.
    Path,

    // 사용자가 골랐다. 저장하면 파일에 적힌다.
    User,
}

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<GameProfile> Profiles { get; } =
        new[] { GameProfile.None }.Concat(GameProfileCatalog.Default.Profiles).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileVerificationText))]
    [NotifyPropertyChangedFor(nameof(IsProfileAssumed))]
    private GameProfile _selectedProfile = GameProfile.None;

    public HoldProfileOrigin ProfileOrigin { get; private set; }

    // 불러오는 중에 프로파일을 맞추는 것은 사용자가 고친 것이 아니다.
    // 이 표시 없이 SelectedProfile 을 바꾸면, 경로로 추정한 프로파일까지 헤더에 박혀 저장된다.
    private bool _isApplyingProfile;

    partial void OnSelectedProfileChanged(GameProfile value)
    {
        // 콤보박스가 템플릿을 갈아끼우는 순간 null 을 밀어 넣는 일이 있다. 빈 프로파일로 되돌린다.
        if (value is null)
        {
            SelectedProfile = GameProfile.None;
            return;
        }

        if (!_isApplyingProfile)
        {
            Chart.Header.ProfileId = value.Id;
            SetProfileOrigin(value.IsNone ? HoldProfileOrigin.None : HoldProfileOrigin.User);
            MarkDirty();
        }

        RecomputeHolds();
    }

    // 문서를 열거나 비울 때 프로파일을 정한다.
    //
    // 파일에 #BMSEDITER_PROFILE 이 있으면 그것을 따르고, 없으면 경로로 추정한다.
    // 실제 게임 차트는 게임 폴더 아래(…\DEFLATE\hwa\곡\hwa2.bms)에 있어서 헤더 없이도 알아볼 수 있다.
    // 추정은 화면에만 쓰고 헤더에는 넣지 않는다 — 열었다 바로 저장한 파일이 원본과 같아야 한다.
    private void ApplyProfileForDocument(string? filePath)
    {
        var headerId = Chart.Header.ProfileId;
        var profile = GameProfile.None;
        HoldProfileOrigin origin;

        if (!string.IsNullOrWhiteSpace(headerId))
        {
            var found = FindProfile(headerId);
            profile = found ?? GameProfile.None;
            origin = found is null ? HoldProfileOrigin.UnknownHeader : HoldProfileOrigin.Header;
        }
        else if (GameProfileCatalog.Default.DetectFromPath(filePath) is { } detected)
        {
            profile = FindProfile(detected.Id) ?? detected;
            origin = HoldProfileOrigin.Path;
        }
        else
        {
            origin = HoldProfileOrigin.None;
        }

        _isApplyingProfile = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _isApplyingProfile = false;
        }

        SetProfileOrigin(origin);

        // 같은 프로파일이면 위 대입이 변경 알림을 내지 않는다. 노트는 바뀌었으니 직접 다시 읽는다.
        RecomputeHolds();
    }

    private GameProfile? FindProfile(string id) =>
        Profiles.FirstOrDefault(p => !p.IsNone && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private void SetProfileOrigin(HoldProfileOrigin origin)
    {
        ProfileOrigin = origin;
        OnPropertyChanged(nameof(ProfileOrigin));
        OnPropertyChanged(nameof(ProfileOriginText));
    }

    public string ProfileOriginText => ProfileOrigin switch
    {
        HoldProfileOrigin.Header => "파일의 #BMSEDITER_PROFILE 에서 읽었습니다.",
        HoldProfileOrigin.UnknownHeader =>
            $"파일에 적힌 프로파일 '{Chart.Header.ProfileId}' 을 이 에디터가 모릅니다. 헤더는 그대로 두고 홀드는 읽지 않습니다.",
        HoldProfileOrigin.Path => "차트 경로로 게임을 추정했습니다. 저장해도 파일에 적지 않습니다.",
        HoldProfileOrigin.User => "직접 골랐습니다. 저장하면 #BMSEDITER_PROFILE 로 파일에 적힙니다.",
        _ => "게임을 고르면 그 게임의 규칙으로 홀드 짝을 읽어 격자에 몸통으로 그립니다.",
    };

    // 규칙을 얼마나 믿을 수 있는지. 틀린 프로파일은 "아무것도 안 보임"보다 나쁜 "자신 있게 틀린 몸통"을 그린다.
    public string ProfileVerificationText => SelectedProfile?.Verification switch
    {
        _ when SelectedProfile is null || SelectedProfile.IsNone => "",
        ProfileVerification.Playtested => "✅ 실제 채보를 만들어 게임에서 확인한 규칙입니다.",
        ProfileVerification.Source => "게임 모드 코드로 확인한 규칙입니다. 실제 플레이로는 아직 확인 전입니다.",
        _ => "⚠️ 짝 규칙이 게임 코드로 확인되지 않았습니다. 화면의 홀드 몸통을 그대로 믿지 마십시오.",
    };

    public bool IsProfileAssumed =>
        SelectedProfile is { IsNone: false, Verification: ProfileVerification.Assumed };

    public HoldPairingResult HoldResult { get; private set; } = HoldPairingResult.Empty;

    public IReadOnlyList<HoldLink> HoldLinks => HoldResult.Links;

    public IReadOnlyList<HoldDiagnostic> HoldDiagnostics => HoldResult.Diagnostics;

    public bool HasHoldDiagnostics => HoldResult.Diagnostics.Count > 0;

    // 격자에 경고 테두리를 칠 노트. 오류 수준만 칠한다. 주의까지 칠하면 경고가 풍경이 된다.
    public IReadOnlyList<BmsNote> HoldProblemNotes { get; private set; } = Array.Empty<BmsNote>();

    public string HoldSummaryText { get; private set; } = "";

    private HashSet<BmsNote> _holdNotes = new();

    // 검색 창의 "롱" 필터가 쓴다. 노트 종류가 아니라 짝으로 판정한다.
    public bool IsHoldNote(BmsNote note) => _holdNotes.Contains(note);

    // 검사기 목록에서 고른 항목. 고르면 그 노트를 선택하고 격자를 그 자리로 옮긴다.
    [ObservableProperty] private HoldDiagnostic? _selectedHoldDiagnostic;

    partial void OnSelectedHoldDiagnosticChanged(HoldDiagnostic? value)
    {
        if (value is not null && Chart.Notes.Contains(value.Note))
            SetNoteSelection(new[] { value.Note }, NoteSelectionSource.Search);
    }

    private void RecomputeHolds()
    {
        var result = HoldPairingEngine.Pair(Chart.Notes, BuildWavTexts(), SelectedProfile);

        HoldResult = result;
        _holdNotes = new HashSet<BmsNote>(result.Links.SelectMany(link => new[] { link.Head, link.Tail }));
        HoldProblemNotes = result.Diagnostics
            .Where(d => d.Severity == HoldDiagnosticSeverity.Error)
            .Select(d => d.Note)
            .Distinct()
            .ToArray();
        HoldSummaryText = BuildHoldSummary(result);

        OnPropertyChanged(nameof(HoldResult));
        OnPropertyChanged(nameof(HoldLinks));
        OnPropertyChanged(nameof(HoldDiagnostics));
        OnPropertyChanged(nameof(HasHoldDiagnostics));
        OnPropertyChanged(nameof(HoldProblemNotes));
        OnPropertyChanged(nameof(HoldSummaryText));
    }

    private string BuildHoldSummary(HoldPairingResult result)
    {
        if (SelectedProfile is null || SelectedProfile.IsNone)
            return "";

        var errors = result.Diagnostics.Count(d => d.Severity == HoldDiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count - errors;

        var text = $"홀드 짝 {result.Links.Count}개";
        text += errors > 0 ? $" · 🔴 문제 {errors}" : " · 문제 없음";
        if (warnings > 0)
            text += $" · 🟡 주의 {warnings}";

        return text;
    }

    // 키 -> #WAV 에 적힌 글자 그대로. 파일명 규칙은 게임마다 폴더를 떼기도 하고 안 떼기도 해서
    // 원문을 넘기고 떼는 일은 규칙 쪽에 맡긴다.
    private IReadOnlyDictionary<string, string> BuildWavTexts()
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in WavList)
            texts[item.Key] = string.IsNullOrEmpty(item.SourceText) ? item.FileName : item.SourceText;

        return texts;
    }
}
