using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 음원을 읽는 동안 문서가 바뀌었을 때. (2026-09-28 점검)
//
// 디코딩은 몇 초가 걸리고 그동안 창은 살아 있다. 예전에는 끝난 순서대로 음원이 붙어서,
// 폴더를 연달아 열면 앞 곡의 음원이 뒤 차트에 붙을 수 있었다. 디코딩이 끝나는 순서를
// 테스트가 정하려고 DecodeOgg 를 바꿔 끼운다.
public sealed class OggLoadingTests : IDisposable
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OggAudioData>> _decodes = new();

    public OggLoadingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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

    // 테스트가 풀어 줄 때까지 끝나지 않는 디코딩.
    private TaskCompletionSource<OggAudioData> Decode(string path) =>
        _decodes.GetOrAdd(path, _ => new TaskCompletionSource<OggAudioData>(TaskCreationOptions.RunContinuationsAsynchronously));

    private MainWindowViewModel Owner() =>
        new() { DecodeOgg = path => Decode(path).Task.GetAwaiter().GetResult() };

    private static OggAudioData Silence(double seconds)
    {
        const int sampleRate = 8000;
        var frames = (int)(seconds * sampleRate);
        return new OggAudioData(new byte[frames * 4], sampleRate, 2, seconds);
    }

    private string WriteChart(string name)
    {
        var folder = Path.Combine(_directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "hwa2.bms");
        File.WriteAllText(path, $"#TITLE {name}\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task 음원을_읽는_사이_다른_차트를_열면_앞_곡의_음원은_붙지_않는다()
    {
        var owner = Owner();

        Assert.True(owner.LoadBms(WriteChart("a")));
        var loadA = owner.LoadOggAsync("a.ogg");
        Assert.True(owner.IsLoadingOgg);

        Assert.True(owner.LoadBms(WriteChart("b")));
        var loadB = owner.LoadOggAsync("b.ogg");

        Decode("b.ogg").SetResult(Silence(10));
        Assert.Equal(OggLoadResult.Loaded, await loadB);

        // 앞 곡이 늦게 끝나도 뒤 차트의 음원을 덮어쓰지 않는다.
        Decode("a.ogg").SetResult(Silence(20));
        Assert.Equal(OggLoadResult.Superseded, await loadA);

        Assert.Equal("b.ogg", owner.OggFileName);
        Assert.Equal(10, owner.OggDurationSeconds, 6);
        Assert.False(owner.IsLoadingOgg);
    }

    [Fact]
    public async Task 음원을_읽는_사이_새로_만들면_그_음원은_버린다()
    {
        var owner = Owner();

        Assert.True(owner.LoadBms(WriteChart("a")));
        var load = owner.LoadOggAsync("a.ogg");

        await owner.NewFileCommand.ExecuteAsync(null);

        Decode("a.ogg").SetResult(Silence(20));
        Assert.Equal(OggLoadResult.Superseded, await load);

        Assert.Null(owner.OggFileName);
        Assert.Equal(0, owner.OggDurationSeconds);
        Assert.False(owner.IsLoadingOgg);
    }

    [Fact]
    public async Task 음원을_두_번_고르면_나중에_고른_것이_이기고_앞의_실패는_알리지_않는다()
    {
        var owner = Owner();
        Assert.True(owner.LoadBms(WriteChart("a")));

        var first = owner.LoadOggAsync("first.ogg");
        var second = owner.LoadOggAsync("second.ogg");

        Decode("second.ogg").SetResult(Silence(10));
        Assert.Equal(OggLoadResult.Loaded, await second);

        // 사용자는 이미 다른 음원을 골랐다. 버려진 쪽이 깨졌다고 창을 띄울 까닭이 없다.
        Decode("first.ogg").SetException(new InvalidDataException("깨진 파일"));
        Assert.Equal(OggLoadResult.Superseded, await first);

        Assert.Null(owner.LastErrorMessage);
        Assert.Equal("second.ogg", owner.OggFileName);
    }

    [Fact]
    public async Task 읽지_못한_음원은_실패로_알리고_물려_있던_음원은_그대로_둔다()
    {
        var owner = Owner();
        Assert.True(owner.LoadBms(WriteChart("a")));

        var good = owner.LoadOggAsync("good.ogg");
        Decode("good.ogg").SetResult(Silence(10));
        Assert.Equal(OggLoadResult.Loaded, await good);

        var broken = owner.LoadOggAsync("broken.ogg");
        Decode("broken.ogg").SetException(new InvalidDataException("깨진 파일"));

        Assert.Equal(OggLoadResult.Failed, await broken);
        Assert.Equal("깨진 파일", owner.LastErrorMessage);
        Assert.Equal("good.ogg", owner.OggFileName);
        Assert.False(owner.IsLoadingOgg);
    }
}
