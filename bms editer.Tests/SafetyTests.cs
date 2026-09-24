using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 안전장치 묶음(알려진 문제 S-1·S-3·S-4·S-5·S-7)을 못 박아 두는 테스트.
//
// 한 곡에 네 시간이 걸리는 도구인데, 예전에는 앱이 꺼지면 저장 안 한 작업이 통째로 사라졌고,
// 저장을 두 번 하면 원본 옆의 .bak 한 벌마저 덮여 되돌릴 방법이 없었다.
// 여기서 새는 것은 사용자가 알아챌 때쯤이면 이미 늦는다.
//
// 모든 파일은 임시 폴더에만 쓴다. 사용자의 %LocalAppData% 는 건드리지 않는다.
public sealed class SafetyTests : IDisposable
{
    private readonly string _directory;
    private DateTime _clock = new(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc);

    public SafetyTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string RecoveryRoot => Path.Combine(_directory, "recovery");

    // 부를 때마다 1초씩 흐르는 시계. 같은 밀리초에 스냅숏이 겹쳐 순서가 흔들리지 않게 한다.
    private DocumentSnapshotStore Store(string sessionId, Func<string, bool>? isAlive = null) =>
        new(RecoveryRoot, sessionId, isAlive ?? (_ => false), () => _clock = _clock.AddSeconds(1));

    private string WriteChart(string content, string folder = "song", string fileName = "hwa2.bms", Encoding? encoding = null)
    {
        var directory = Path.Combine(_directory, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static string Notes(int count) => string.Concat(Enumerable.Repeat("01", count));

    // ── S-3 자동 저장: 저장소 ────────────────────────────────────────────────

    [Fact]
    public void 자동_저장본은_종류마다_최근_10벌만_남는다()
    {
        var store = Store("s1");
        var original = Path.Combine(_directory, "song", "hwa2.bms");

        for (var i = 0; i < DocumentSnapshotStore.KeepPerKind + 2; i++)
            store.WriteSnapshot(original, $"#TITLE {i}\r\n", new UTF8Encoding(false));

        var kept = store.ListSnapshots(original, SnapshotKind.Auto);

        Assert.Equal(DocumentSnapshotStore.KeepPerKind, kept.Count);
        Assert.Equal("#TITLE 11\r\n", File.ReadAllText(kept[0].SnapshotPath));
        Assert.Equal("#TITLE 2\r\n", File.ReadAllText(kept[^1].SnapshotPath));
    }

    [Fact]
    public void 곡_폴더가_달라도_파일명이_같으면_섞이지_않는다()
    {
        // 이 PC 의 게임 차트는 전부 hwa2.bms 다. 이름만으로 폴더를 나누면 곡끼리 섞인다.
        var store = Store("s1");
        var songA = Path.Combine(_directory, "A", "hwa2.bms");
        var songB = Path.Combine(_directory, "B", "hwa2.bms");

        store.WriteSnapshot(songA, "A", new UTF8Encoding(false));
        store.WriteSnapshot(songB, "B", new UTF8Encoding(false));

        Assert.NotEqual(store.GetDocumentDirectory(songA), store.GetDocumentDirectory(songB));
        Assert.Equal("A", File.ReadAllText(Assert.Single(store.ListSnapshots(songA, SnapshotKind.Auto)).SnapshotPath));
        Assert.Equal("B", File.ReadAllText(Assert.Single(store.ListSnapshots(songB, SnapshotKind.Auto)).SnapshotPath));
    }

    [Fact]
    public void 정상_종료한_실행은_복구를_묻지_않는다()
    {
        var before = Store("before");
        before.BeginSession();
        before.UpdateSession(before.WriteSnapshot(null, "x", new UTF8Encoding(false)));
        before.EndSession();

        Assert.Empty(Store("after").FindAbandonedSessions());
    }

    [Fact]
    public void 죽은_실행이_남긴_저장_안_된_작업을_찾아낸다()
    {
        var crashed = Store("crashed");
        crashed.BeginSession();
        var snapshot = crashed.WriteSnapshot(Path.Combine(_directory, "song", "hwa2.bms"), "#TITLE 살려줘\r\n", new UTF8Encoding(false));
        crashed.UpdateSession(snapshot);

        var next = Store("next");
        var found = Assert.Single(next.FindAbandonedSessions());

        Assert.Equal("crashed", found.SessionId);
        Assert.Equal(snapshot.SnapshotPath, found.Snapshot.SnapshotPath);

        // 한 번 정리하면 다시 묻지 않는다. 자동 저장본은 이력으로 남는다.
        next.DismissSession(found);
        Assert.Empty(next.FindAbandonedSessions());
        Assert.True(File.Exists(snapshot.SnapshotPath));
    }

    [Fact]
    public void 아직_살아_있는_실행은_건드리지_않는다()
    {
        // 창을 두 개 띄운 경우다. 다른 창의 작업을 "죽었다"고 복구하면 안 된다.
        var running = Store("running");
        running.UpdateSession(running.WriteSnapshot(null, "x", new UTF8Encoding(false)));

        var other = Store("other", isAlive: id => id == "running");

        Assert.Empty(other.FindAbandonedSessions());
        Assert.True(File.Exists(Path.Combine(RecoveryRoot, "sessions", "running.json")));
    }

    [Fact]
    public void 되살릴_것이_없는_세션_파일은_치운다()
    {
        var crashed = Store("crashed");
        crashed.BeginSession(); // 저장 안 된 작업 없이 죽었다

        Assert.Empty(Store("next").FindAbandonedSessions());
        Assert.False(File.Exists(Path.Combine(RecoveryRoot, "sessions", "crashed.json")));
    }

    [Fact]
    public void 복구를_기다리는_자동_저장본은_개수가_넘쳐도_지우지_않는다()
    {
        var original = Path.Combine(_directory, "song", "hwa2.bms");

        var crashed = Store("crashed");
        var waiting = crashed.WriteSnapshot(original, "살려줘", new UTF8Encoding(false));
        crashed.UpdateSession(waiting);

        // 복구를 미룬 채 같은 곡을 한참 더 고쳤다.
        var next = Store("next");
        for (var i = 0; i < DocumentSnapshotStore.KeepPerKind + 5; i++)
            next.WriteSnapshot(original, $"새 작업 {i}", new UTF8Encoding(false));

        Assert.True(File.Exists(waiting.SnapshotPath));
        Assert.Single(next.FindAbandonedSessions());
    }

    [Fact]
    public void 한_달_넘게_안_쓴_문서의_자동_저장본은_지운다()
    {
        var store = Store("s1");
        var old = store.WriteSnapshot(Path.Combine(_directory, "old", "hwa2.bms"), "old", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(old.SnapshotPath, _clock - TimeSpan.FromDays(40));
        File.SetLastWriteTimeUtc(old.SnapshotPath + ".json", _clock - TimeSpan.FromDays(40));

        var fresh = store.WriteSnapshot(Path.Combine(_directory, "fresh", "hwa2.bms"), "fresh", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(fresh.SnapshotPath, _clock);
        File.SetLastWriteTimeUtc(fresh.SnapshotPath + ".json", _clock);

        store.PruneOldDocuments();

        Assert.False(Directory.Exists(Path.GetDirectoryName(old.SnapshotPath)));
        Assert.True(File.Exists(fresh.SnapshotPath));
    }

    // ── S-3 자동 저장: 뷰모델 ────────────────────────────────────────────────

    [Fact]
    public void 저장소가_없으면_디스크를_건드리지_않는다()
    {
        // 테스트나 디자이너에서 만든 뷰모델이 사용자의 %LocalAppData% 에 쓰면 안 된다.
        var vm = new MainWindowViewModel();
        vm.Title = "고침";

        Assert.True(vm.IsDirty);
        Assert.Null(vm.AutoSave());
    }

    [Fact]
    public void 자동_저장은_저장_안_된_변경이_있을_때만_쓴다()
    {
        var path = WriteChart("#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01000000\r\n");
        var store = Store("s1");
        var vm = new MainWindowViewModel { Snapshots = store };
        Assert.True(vm.LoadBms(path));

        Assert.Null(vm.AutoSave());

        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("12", 1, 0.0));
        var first = vm.AutoSave();
        Assert.NotNull(first);

        // 그 뒤로 바뀐 게 없으면 같은 것을 또 쓰지 않는다.
        var second = vm.AutoSave();
        Assert.Equal(first!.SnapshotPath, second!.SnapshotPath);
        Assert.Single(store.ListSnapshots(path, SnapshotKind.Auto));
    }

    [Fact]
    public void 저장하면_복구할_작업이_없다고_적는다()
    {
        var path = WriteChart("#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n");
        var vm = new MainWindowViewModel { Snapshots = Store("s1") };
        Assert.True(vm.LoadBms(path));

        vm.Title = "고침";
        Assert.NotNull(vm.AutoSave());
        Assert.Single(Store("probe").FindAbandonedSessions());

        Assert.True(vm.SaveBms(path));
        Assert.Empty(Store("probe").FindAbandonedSessions());
    }

    [Fact]
    public void 자동_저장본을_되살리면_원래_자리_인코딩_키음_경로가_그대로다()
    {
        // CP949 차트에 한글 키음. 자동 저장본은 %LocalAppData% 에 있지만
        // 원래 곡 폴더에 있는 것처럼 읽어야 키음을 찾는다.
        var cp949 = Encoding.GetEncoding(949);
        var path = WriteChart("#TITLE 한글 제목\r\n#BPM 120\r\n#WAV01 소리.wav\r\n#00111:01000000\r\n", encoding: cp949);
        var keySound = Path.Combine(Path.GetDirectoryName(path)!, "소리.wav");
        File.WriteAllText(keySound, "not really audio");

        var before = new MainWindowViewModel { Snapshots = Store("crashed") };
        Assert.True(before.LoadBms(path));
        Assert.Equal(949, before.DocumentEncoding.CodePage);
        before.PlaceNoteCommand.Execute(new NotePlacementArgs("12", 1, 0.0));
        Assert.NotNull(before.AutoSave());

        // 여기서 앱이 죽었다. 다음 실행.
        var nextStore = Store("next");
        var abandoned = Assert.Single(nextStore.FindAbandonedSessions());

        var after = new MainWindowViewModel { Snapshots = nextStore };
        Assert.True(after.RecoverFromSnapshot(abandoned.Snapshot));

        Assert.Equal(path, after.CurrentFilePath);
        Assert.True(after.IsDirty); // 원래 파일에는 아직 안 들어갔다
        Assert.Equal(2, after.Chart.Notes.Count);
        Assert.Equal("한글 제목", after.Title);
        Assert.Equal(949, after.DocumentEncoding.CodePage);
        Assert.Equal(keySound, after.Chart.WavTable["01"]);

        // 원래 파일은 사용자가 저장하기 전까지 그대로다.
        Assert.Single(BmsParser.Parse(path).Chart.Notes);

        Assert.True(after.SaveBms(path));
        var saved = BmsParser.Parse(path);
        Assert.Equal(2, saved.Chart.Notes.Count);
        Assert.Equal(949, saved.Encoding.CodePage);
        Assert.Contains("#WAV01 소리.wav", cp949.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void 제목_없는_문서도_자동_저장하고_되살린다()
    {
        var keySound = Path.Combine(_directory, "keys", "kick.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(keySound)!);
        File.WriteAllText(keySound, "not really audio");

        var before = new MainWindowViewModel { Snapshots = Store("crashed") };
        Assert.True(before.AddWav(keySound));
        before.PlaceNoteCommand.Execute(new NotePlacementArgs("11", 0, 0.5));
        var snapshot = before.AutoSave();
        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.OriginalPath);

        var after = new MainWindowViewModel { Snapshots = Store("next") };
        Assert.True(after.RecoverFromSnapshot(snapshot));

        // 경로 없이 되살려야 [저장]이 자동 저장 폴더에 쓰지 않고 저장 위치를 묻는다.
        Assert.Null(after.CurrentFilePath);
        Assert.True(after.IsDirty);
        Assert.Single(after.Chart.Notes);
        Assert.Equal(keySound, after.Chart.WavTable[after.Chart.Notes[0].WavKey]);
    }

    // ── S-4 저장 이력 ────────────────────────────────────────────────────────

    [Fact]
    public void 저장을_여러_번_해도_덮어쓰기_전_내용이_이력으로_남는다()
    {
        var path = WriteChart("#TITLE v0\r\n#BPM 120\r\n");
        var store = Store("s1");
        var vm = new MainWindowViewModel { Snapshots = store };
        Assert.True(vm.LoadBms(path));

        vm.Title = "v1";
        Assert.True(vm.SaveBms(path));
        vm.Title = "v2";
        Assert.True(vm.SaveBms(path));

        // .bak 은 한 벌뿐이라 v1 만 남는다. 이력에는 v1 과 v0 이 다 있다.
        Assert.Contains("#TITLE v1", File.ReadAllText(path + SafeFileWriter.BackupExtension));

        var history = store.ListSnapshots(path, SnapshotKind.BeforeSave);
        Assert.Equal(2, history.Count);
        Assert.Contains("#TITLE v1", File.ReadAllText(history[0].SnapshotPath));
        Assert.Contains("#TITLE v0", File.ReadAllText(history[1].SnapshotPath));
    }

    [Fact]
    public void 바꾼_것_없이_저장을_거듭해도_이력이_같은_것으로_차지_않는다()
    {
        var path = WriteChart("#TITLE v0\r\n#BPM 120\r\n");
        var store = Store("s1");
        var vm = new MainWindowViewModel { Snapshots = store };
        Assert.True(vm.LoadBms(path));

        for (var i = 0; i < 5; i++)
            Assert.True(vm.SaveBms(path));

        // 첫 저장이 원본(v0)을, 두 번째가 저장된 내용을 남긴다. 그 뒤로는 똑같아서 더 늘지 않는다.
        Assert.Equal(2, store.ListSnapshots(path, SnapshotKind.BeforeSave).Count);
    }

    [Fact]
    public void 이력을_남기지_못해도_저장은_된다()
    {
        // 안전장치가 실패했다고 저장이 막히면 본말전도다.
        var path = WriteChart("#TITLE v0\r\n#BPM 120\r\n");
        var blocker = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blocker, "파일이라 폴더를 만들 수 없다");

        var vm = new MainWindowViewModel { Snapshots = new DocumentSnapshotStore(blocker, "s1", _ => false) };
        Assert.True(vm.LoadBms(path));
        vm.Title = "v1";

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        Assert.Contains("#TITLE v1", File.ReadAllText(path));
    }

    // ── S-5 검색 창의 일괄 삭제·번호 변경 ────────────────────────────────────

    private MainWindowViewModel OwnerWithNotes(int count)
    {
        var path = WriteChart($"#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n#00111:{Notes(count)}\r\n", folder: Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.Equal(count, vm.Chart.Notes.Count);
        return vm;
    }

    [Fact]
    public async Task 많은_노트를_지울_때는_먼저_묻고_거절하면_그대로_둔다()
    {
        var owner = OwnerWithNotes(NoteSearchViewModel.ConfirmThreshold);
        string? asked = null;
        owner.ConfirmAsync = message => { asked = message; return Task.FromResult(false); };
        var search = new NoteSearchViewModel(owner);

        await search.DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Contains($"{NoteSearchViewModel.ConfirmThreshold}개", asked);
        Assert.Equal(NoteSearchViewModel.ConfirmThreshold, owner.Chart.Notes.Count);
        Assert.Equal("지우지 않았습니다.", search.StatusMessage);
    }

    [Fact]
    public async Task 확인하면_많은_노트도_지운다()
    {
        var owner = OwnerWithNotes(NoteSearchViewModel.ConfirmThreshold);
        owner.ConfirmAsync = _ => Task.FromResult(true);
        var search = new NoteSearchViewModel(owner);

        await search.DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Empty(owner.Chart.Notes);
    }

    [Fact]
    public async Task 확인_창을_띄울_수_없으면_많은_노트를_지우지_않는다()
    {
        var owner = OwnerWithNotes(NoteSearchViewModel.ConfirmThreshold);
        var search = new NoteSearchViewModel(owner);

        await search.DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Equal(NoteSearchViewModel.ConfirmThreshold, owner.Chart.Notes.Count);
    }

    [Fact]
    public async Task 적은_노트는_묻지_않고_지운다()
    {
        var owner = OwnerWithNotes(NoteSearchViewModel.ConfirmThreshold - 1);
        owner.ConfirmAsync = _ => throw new InvalidOperationException("묻지 말아야 한다");
        var search = new NoteSearchViewModel(owner);

        await search.DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Empty(owner.Chart.Notes);
    }

    [Fact]
    public async Task 번호_일괄_변경도_많으면_먼저_묻는다()
    {
        var owner = OwnerWithNotes(NoteSearchViewModel.ConfirmThreshold);
        var answer = false;
        owner.ConfirmAsync = _ => Task.FromResult(answer);
        var search = new NoteSearchViewModel(owner) { ReplacementWavKey = "02" };

        await search.ReplaceWavKeyCommand.ExecuteAsync(null);
        Assert.All(owner.Chart.Notes, note => Assert.Equal("01", note.WavKey));
        Assert.Equal("바꾸지 않았습니다.", search.StatusMessage);

        answer = true;
        await search.ReplaceWavKeyCommand.ExecuteAsync(null);
        Assert.All(owner.Chart.Notes, note => Assert.Equal("02", note.WavKey));
    }

    // ── S-2 오류 기록 ────────────────────────────────────────────────────────

    [Fact]
    public void 오류_기록은_한_건씩_남기고_최근_것만_유지한다()
    {
        var logs = Path.Combine(_directory, "logs");

        for (var i = 0; i < ErrorLog.KeepCount + 5; i++)
            Assert.NotNull(ErrorLog.Write(logs, new InvalidOperationException($"오류 {i}"), "테스트"));

        Assert.Equal(ErrorLog.KeepCount, Directory.GetFiles(logs, "error-*.log").Length);
    }

    [Fact]
    public void 같은_오류가_연달아_나면_한_번만_적는다()
    {
        // 렌더나 타이머에서 매번 터지는 예외가 기록 폴더를 채우지 않게.
        var logs = Path.Combine(_directory, "logs");
        var exception = new InvalidOperationException("매 프레임 터짐");

        Assert.NotNull(ErrorLog.Write(logs, exception, "테스트"));
        Assert.Null(ErrorLog.Write(logs, exception, "테스트"));
        Assert.Single(Directory.GetFiles(logs));
    }

    [Fact]
    public void 오류_기록을_못_남겨도_던지지_않는다()
    {
        var blocker = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blocker, "파일이라 폴더를 만들 수 없다");

        Assert.Null(ErrorLog.Write(blocker, new InvalidOperationException("원래 오류"), "테스트"));
    }

    // ── S-7 종료할 때 키음 믹서 정리 ─────────────────────────────────────────

    [Fact]
    public void 키음이_울리는_중에_정리해도_예외_없이_끝난다()
    {
        var path = Path.Combine(_directory, "long.wav");
        WriteSilentWav(path, frames: 44100);

        var player = new KeySoundPlayer();
        player.Play(path);
        player.Play(path);
        System.Threading.Thread.Sleep(50);

        player.Dispose();
        player.Dispose();
        player.Play(path); // 정리한 뒤에 불려도 조용히 무시한다
    }

    private static void WriteSilentWav(string path, int frames)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var dataSize = frames * 4;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(44100);
        writer.Write(44100 * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
