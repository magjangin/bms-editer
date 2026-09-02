namespace bms_editer.Models;

// 에디터가 해석하지 않는 원본 줄을 그대로 보관한다.
//
// 이 에디터는 1P 건반 채널(11~18, 16)만 편집한다. 그런데 저장할 때 파일을
// 통째로 다시 만들기 때문에, 보관하지 않으면 BGM(#xxx01)·마디 길이(#xxx02)·
// BPM 변화(#xxx03/08)·STOP·BGA·롱노트·2P 채널과 #TOTAL 같은 확장 헤더가
// 전부 사라진다. 원문을 그대로 들고 있다가 다시 내보내 손실을 막는다.
public sealed class BmsRawLine
{
    // 파일에 있던 줄 그대로(앞뒤 공백만 제거).
    public string Text { get; set; } = string.Empty;

    // 데이터 줄(#mmmCC:...)이면 마디 번호, 헤더 줄·주석이면 -1.
    public int Measure { get; set; } = -1;

    public bool IsData => Measure >= 0;
}
