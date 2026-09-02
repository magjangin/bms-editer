using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 파형 버킷과 시각의 대응을 못 박아 두는 테스트. (알려진 문제 27번)
//
// 이 계열은 세 번 어긋났다. f86a58c(53ms), e19c25b(12ms), 그리고 27번(12~15ms).
// 전부 "같은 규칙을 다른 데서 다시 쓰다가" 생겼고, 눈으로는 거의 안 보이는데
// 씽크를 맞추는 기준선이라 결과는 크다. 그래서 규칙 자체에 테스트를 건다.
public sealed class WaveformTimeAxisTests
{
    [Theory]
    [InlineData(0, 10, 0.0)]
    [InlineData(1, 10, 0.1)]
    [InlineData(5, 10, 0.5)]
    [InlineData(9, 10, 0.9)]
    public void 버킷_i_는_i_나누기_count_시점에_대응한다(int index, int count, double expected)
    {
        Assert.Equal(expected, OggPeakLoader.GetBucketRatio(index, count), 12);
    }

    [Fact]
    public void 마지막_버킷은_끝이_아니라_한_칸_앞이다()
    {
        // i/(count-1) 로 잘못 쓰면 마지막 버킷이 1.0 이 되어 곡 전체가 한 칸씩 당겨진다.
        Assert.Equal(0.9999, OggPeakLoader.GetBucketRatio(9999, 10000), 12);
        Assert.NotEqual(1.0, OggPeakLoader.GetBucketRatio(9999, 10000));
    }

    [Theory]
    [InlineData(60.0, 4800)]
    [InlineData(180.0, 14400)]
    [InlineData(300.0, 20000)]
    public void 잘못된_공식과의_차이가_곡_끝에서_10ms를_넘는다(double durationSeconds, int count)
    {
        // 이 테스트는 "왜 이게 중요한가"를 숫자로 남겨둔다.
        // 옛 공식 i/(count-1) 과 옳은 공식 i/count 의 차이는 곡 끝에서 최대 duration/count 다.
        var last = count - 1;
        var correct = OggPeakLoader.GetBucketRatio(last, count) * durationSeconds;
        var buggy = (double)last / (count - 1) * durationSeconds;

        var driftMs = (buggy - correct) * 1000;

        Assert.True(driftMs > 10.0, $"곡 끝 어긋남이 {driftMs:F1}ms 로 예상보다 작다");
        Assert.True(driftMs < 20.0, $"곡 끝 어긋남이 {driftMs:F1}ms 로 예상보다 크다");
    }

    [Fact]
    public void 버킷_수가_0이면_0을_돌려준다()
    {
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(0, 0));
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(5, -1));
    }
}
