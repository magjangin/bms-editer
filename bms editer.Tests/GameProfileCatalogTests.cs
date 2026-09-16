using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services.Holds;
using Xunit;

namespace bms_editer.Tests;

// 게임 프로파일을 읽고 고르는 쪽의 테스트.
//
// 프로파일이 조용히 빠지면 "홀드가 하나도 안 잡힌다"로만 드러난다. 원인이 화면에 없어서
// 사용자는 차트를 의심한다. 그래서 읽기·검증·경로 추정을 따로 못 박는다.
public sealed class GameProfileCatalogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));

    public GameProfileCatalogTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public void 내장_프로파일이_전부_문제없이_읽힌다()
    {
        var catalog = GameProfileCatalog.Load(overrideDirectory: null);

        Assert.Empty(catalog.LoadErrors);
        Assert.Equal(
            new[] { "deflate", "gunvolt", "muse_dash", "stargazer", "startrail", "unbeatable" },
            catalog.Profiles.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void 규칙을_어디서_옮겼는지와_믿을_수_있는_정도가_적혀_있다()
    {
        var catalog = GameProfileCatalog.Load(overrideDirectory: null);

        Assert.All(catalog.Profiles, p => Assert.False(string.IsNullOrWhiteSpace(p.Source), p.Id));

        // 실제로 채보를 만들어 본 게임은 뮤즈 대시 하나다.
        Assert.Equal(ProfileVerification.Playtested, catalog.Find("muse_dash")!.Verification);

        // 스타게이저는 모드가 짝을 맺지 않고 게임에 넘긴다. 짝 규칙은 가정이다.
        Assert.Equal(ProfileVerification.Assumed, catalog.Find("stargazer")!.Verification);

        foreach (var id in new[] { "deflate", "startrail", "gunvolt", "unbeatable" })
            Assert.Equal(ProfileVerification.Source, catalog.Find(id)!.Verification);
    }

    [Theory]
    [InlineData(@"H:\steam\steamapps\common\DEFLATE\hwa\どりーむもーど\hwa2.bms", "deflate")]
    [InlineData(@"H:\steam\steamapps\common\Sixtar Gate STARGAZER\hwa\Discotic Night\hwa2.bms", "stargazer")]
    [InlineData(@"H:\Sixtar Gate STARTRAIL custom mode\hwa\blue\hwa2.bms", "startrail")]
    [InlineData(@"H:\steam\steamapps\common\GUNVOLT RECORDS Cychronicle\hwa\shut up\hwa2.bms", "gunvolt")]
    [InlineData(@"H:\muse dash hwa\hwa\MARENOIA\hwa2.bms", "muse_dash")]
    [InlineData(@"H:\steam\steamapps\common\UNBEATABLE\hwa\fogat\hwa2.bms", "unbeatable")]
    [InlineData("/home/user/games/DEFLATE/hwa/song/hwa2.bms", "deflate")]
    [InlineData(@"H:\steam\steamapps\common\A Dance of Fire and Ice\hwa\hwa2.bms", "")]
    [InlineData(@"C:\charts\my song\chart.bms", "")]
    public void 경로로_게임을_추정한다(string path, string expectedId)
    {
        var detected = GameProfileCatalog.Default.DetectFromPath(path);

        Assert.Equal(expectedId, detected?.Id ?? "");
    }

    [Fact]
    public void 옆_폴더의_프로파일이_같은_id를_덮어쓴다()
    {
        File.WriteAllText(
            Path.Combine(_directory, "deflate.json"),
            """
            {
              "id": "deflate",
              "displayName": "덮어쓴 DEFLATE",
              "holdRules": [ { "kind": "Hold", "role": { "type": "KeyValue", "headKeys": [ "02" ], "tailKeys": [ "03" ] } } ]
            }
            """,
            new UTF8Encoding(false));

        var catalog = GameProfileCatalog.Load(_directory);

        Assert.Empty(catalog.LoadErrors);
        Assert.Equal("덮어쓴 DEFLATE", catalog.Find("deflate")!.DisplayName);
        Assert.Equal(6, catalog.Profiles.Count);
    }

    [Fact]
    public void 망가진_프로파일은_목록에서_빠지고_이유가_남는다()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ \"id\": ", new UTF8Encoding(false));

        var catalog = GameProfileCatalog.Load(_directory);

        Assert.Contains(catalog.LoadErrors, e => e.Contains("broken.json", StringComparison.Ordinal));
        Assert.Equal(6, catalog.Profiles.Count);
    }

    [Theory]
    [InlineData("""{ "id": "x", "holdRules": [] }""")]
    [InlineData("""{ "id": "x", "holdRules": [ { "role": { "type": "KeyValue", "headKeys": [ "02" ] } } ] }""")]
    [InlineData("""{ "id": "x", "holdRules": [ { "role": { "type": "KeyValue", "headKeys": [ "02" ], "tailKeys": [ "03" ] }, "policy": "Alternate" } ] }""")]
    [InlineData("""{ "id": "x", "holdRules": [ { "role": { "type": "FileNameUid" }, "policy": "Alternate" } ] }""")]
    [InlineData("""{ "holdRules": [ { "role": { "type": "KeyValue", "headKeys": [ "02" ], "tailKeys": [ "03" ] } } ] }""")]
    public void 규칙이_빠지거나_짝_방식과_맞지_않으면_읽을_때_거부한다(string json)
    {
        Assert.Throws<FormatException>(() => GameProfileCatalog.Parse(json));
    }
}
