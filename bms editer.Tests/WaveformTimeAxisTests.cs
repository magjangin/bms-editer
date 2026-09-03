using System;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 파형 버킷과 시각의 대응을 못 박아 두는 테스트. (알려진 문제 27번)
//
// 이 계열은 네 번 어긋났다. f86a58c(53ms), e19c25b(12ms), 27번(12~15ms),
// 그리고 파형 블록이 i/(count-1) 을 쓰던 것(반 버킷).
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
    [InlineData(60.0, 24000)]
    [InlineData(180.0, 72000)]
    [InlineData(300.0, 120000)]
    public void 잘못된_공식과의_차이는_버킷_한_칸만큼_쌓인다(double durationSeconds, int count)
    {
        // 이 테스트는 "왜 이게 중요한가"를 숫자로 남겨둔다.
        // 옛 공식 i/(count-1) 과 옳은 공식 i/count 의 차이는 곡 끝에서 정확히 버킷 한 칸이다.
        // 버킷을 잘게 쪼갤수록 작아지지만 0 이 되지는 않으므로, 규칙은 계속 한 군데서만 쓴다.
        var last = count - 1;
        var correct = OggPeakLoader.GetBucketRatio(last, count) * durationSeconds;
        var buggy = (double)last / (count - 1) * durationSeconds;

        var driftMs = (buggy - correct) * 1000;
        var oneBucketMs = durationSeconds / count * 1000;

        Assert.Equal(oneBucketMs, driftMs, 6);
        Assert.True(driftMs > 0);
    }

    [Fact]
    public void 버킷_수가_0이면_0을_돌려준다()
    {
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(0, 0));
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(5, -1));
    }

    [Fact]
    public void 소수_버킷도_같은_규칙을_따른다()
    {
        // 어택 위치는 버킷 두 칸 사이를 보간해서 잡는다. 정수 규칙과 어긋나면 안 된다.
        Assert.Equal(0.25, OggPeakLoader.GetBucketRatio(2.5, 10), 12);
    }

    [Theory]
    [InlineData(100, 800)]   // 줌 아웃: 버킷이 블록보다 8배 많다
    [InlineData(800, 100)]   // 줌 인: 블록이 버킷보다 8배 많다
    [InlineData(333, 1000)]  // 나누어떨어지지 않는 경우
    [InlineData(1000, 1000)]
    public void 화면_블록은_소스_버킷을_빠짐없이_덮는다(int blockCount, int sourceCount)
    {
        var covered = new bool[sourceCount];
        var previousEnd = 0;

        for (var i = 0; i < blockCount; i++)
        {
            var (start, end) = OggPeakLoader.GetBlockSourceRange(i, blockCount, sourceCount);

            Assert.InRange(start, 0, sourceCount - 1);
            Assert.InRange(end, start + 1, sourceCount);

            // 앞 블록이 끝난 자리에서 이어져야 구멍이 생기지 않는다.
            Assert.True(start <= previousEnd, $"블록 {i} 가 {previousEnd} 를 건너뛰고 {start} 에서 시작한다");

            for (var j = start; j < end; j++)
                covered[j] = true;

            previousEnd = Math.Max(previousEnd, end);
        }

        Assert.DoesNotContain(false, covered);
    }

    [Fact]
    public void 블록의_시작_버킷은_같은_시각의_버킷과_일치한다()
    {
        // GetBucketRatio 규칙과 GetBlockSourceRange 가 다른 시각을 가리키면
        // 파형만 반 버킷씩 밀린 채로 격자와 나란히 그려진다.
        const int blockCount = 1000;
        const int sourceCount = 4000;

        for (var i = 0; i < blockCount; i++)
        {
            var (start, _) = OggPeakLoader.GetBlockSourceRange(i, blockCount, sourceCount);
            var expected = (int)(OggPeakLoader.GetBucketRatio(i, blockCount) * sourceCount);

            Assert.Equal(expected, start);
        }
    }

    [Fact]
    public void 줌을_줄여도_어택이_사라지지_않는다()
    {
        // 버킷 800칸 중 한 곳만 튀는 신호. 블록이 100개뿐이라 옛 방식(블록마다 버킷을
        // 하나만 찍어 읽기)은 8칸 중 7칸을 버렸고, 드럼 한 방이 통째로 화면에서 사라졌다.
        var source = new float[800];
        source[503] = 1.0f;

        var pointSampled = 0f;
        var rangeAggregated = 0f;

        for (var i = 0; i < 100; i++)
        {
            // 옛 방식: i/(blockCount-1) 비율로 버킷 하나만 읽는다.
            var pointIndex = (int)Math.Round((double)i / 99 * (source.Length - 1));
            pointSampled = MathF.Max(pointSampled, source[pointIndex]);

            var (start, end) = OggPeakLoader.GetBlockSourceRange(i, 100, source.Length);
            for (var j = start; j < end; j++)
                rangeAggregated = MathF.Max(rangeAggregated, source[j]);
        }

        Assert.Equal(0f, pointSampled);
        Assert.Equal(1f, rangeAggregated);
    }
}
