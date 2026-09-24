using System;
using System.IO;
using bms_editer.Services;

namespace bms_editer.ViewModels;

// 자동 저장 · 저장 이력 · 비정상 종료 복구. (알려진 문제 S-1·S-3·S-4)
//
// 디스크에 쓰는 일이라 앱이 Snapshots 를 붙여 줄 때만 동작한다.
// 테스트에서 만든 뷰모델은 비워 두므로 사용자의 %LocalAppData% 를 건드리지 않는다.
//
// 여기 있는 것은 전부 **던지지 않는다.** 안전장치가 실패했다고 편집이나 저장이 막히면 본말전도다.
// 실패는 오류 기록(ErrorLog)에만 남긴다.
public sealed partial class MainWindowViewModel
{
    public DocumentSnapshotStore? Snapshots { get; set; }

    // 마지막 자동 저장본과 그때의 내용. 그 뒤로 바뀐 게 없으면 또 쓰지 않는다.
    private SnapshotInfo? _lastAutoSnapshot;
    private string? _lastAutoSnapshotContent;

    // 몇 분마다 부른다. 저장 안 된 변경이 없으면 아무것도 안 한다.
    //
    // 스냅숏을 쓰고 나면 세션 파일에 "이 실행에는 저장 안 된 작업이 있고, 최근 것은 이것"이라고 적는다.
    // 앱이 여기서 죽으면 다음 실행이 그걸 보고 복구를 묻는다.
    public SnapshotInfo? AutoSave()
    {
        if (!IsDirty || Snapshots is not { } store)
            return null;

        try
        {
            // 제목 없는 문서는 자동 저장 폴더를 기준으로 경로를 적는다. 되살릴 때도 그 자리에서 읽는다.
            var outputPath = CurrentFilePath ?? Path.Combine(store.GetDocumentDirectory(null), "untitled.bms");
            var content = BuildDocumentText(outputPath);

            var sameDocument = _lastAutoSnapshot?.OriginalPath == FullPathOrNull(CurrentFilePath);
            if (_lastAutoSnapshot is null || !sameDocument || content != _lastAutoSnapshotContent)
            {
                var encoding = ResolveEncodingFor(content, out _);
                _lastAutoSnapshot = store.WriteSnapshot(CurrentFilePath, content, encoding, SnapshotKind.Auto);
                _lastAutoSnapshotContent = content;
            }

            store.UpdateSession(_lastAutoSnapshot);
            return _lastAutoSnapshot;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(store.LogDirectory, ex, "자동 저장");
            return null;
        }
    }

    // 예상하지 못한 예외가 난 순간에 부른다. 다음 자동 저장을 기다리지 않고 바로 남긴다.
    public SnapshotInfo? EmergencySave() => AutoSave();

    // 자동 저장본을 원래 문서처럼 되살린다.
    //
    // 원래 파일은 건드리지 않는다. 되살린 내용은 "저장해야 할 변경"으로 남겨서,
    // 사용자가 확인하고 [저장]을 눌러야 원래 자리에 써진다.
    public bool RecoverFromSnapshot(SnapshotInfo snapshot)
    {
        var documentPath = snapshot.OriginalPath ?? snapshot.SnapshotPath;
        if (!LoadDocument(snapshot.SnapshotPath, documentPath))
            return false;

        // 제목 없는 문서였다면 경로가 없는 채로 둔다. 자동 저장 폴더에 저장되면 안 된다.
        CurrentFilePath = snapshot.OriginalPath;

        // 자동 저장할 때 쓴 인코딩이 곧 원래 문서의 인코딩이다. 감지에 맡기지 않는다.
        if (snapshot.GetEncoding() is { } encoding)
            DocumentEncoding = encoding;

        MarkDirty();
        return true;
    }

    // [저장]이 원본을 덮어쓰기 직전의 파일을 저장 이력으로 옮겨 둔다. (S-4)
    private void KeepCopyBeforeOverwrite(string filePath)
    {
        if (Snapshots is not { } store)
            return;

        try
        {
            store.CopyBeforeOverwrite(filePath);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(store.LogDirectory, ex, "저장 전 사본");
        }
    }

    // 저장했거나 버렸거나 새로 열었다. 되살릴 작업이 없다고 세션에 적는다.
    private void OnDocumentBecameClean()
    {
        if (Snapshots is not { } store)
            return;

        try
        {
            store.UpdateSession(null);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(store.LogDirectory, ex, "세션 기록");
        }
    }

    // 다른 문서로 바뀌었다. 앞 문서의 자동 저장본과 내용을 비교하지 않게 잊는다.
    private void ForgetLastAutoSnapshot()
    {
        _lastAutoSnapshot = null;
        _lastAutoSnapshotContent = null;
    }

    private static string? FullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
