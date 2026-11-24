using System.IO;

namespace bms_editer.Models;

public sealed class BmsWavItem
{
    public string Key { get; set; } = string.Empty;

    private string _filePath = string.Empty;
    private bool _isMissing;

    // 재생에 쓰는 실제 경로. 적힌 자리에 파일이 없으면 하위 폴더에서 찾아 붙인 결과일 수 있다.
    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value ?? string.Empty;
            RefreshFileStatus();
        }
    }

    // 파일에 적혀 있던 경로 문자열 그대로. 저장할 때 되돌려 쓴다.
    // 새로 추가한 키음은 비어 있고, 그때는 FilePath 로 상대경로를 만든다.
    public string SourceText { get; set; } = string.Empty;

    // 적힌 자리에 파일이 없어서 같은 이름을 하위 폴더에서 찾아 붙였는지.
    // 그 추측 결과를 저장 파일에 박으면, 오래된 백업 폴더가 남아 있을 때
    // 차트가 그쪽을 가리키도록 조용히 바뀌어 버린다.
    public bool IsPathGuessed { get; set; }

    public string FileName => Path.GetFileName(_filePath);

    // 매 UI 바인딩마다 동기 File.Exists() 를 부르지 않도록 FilePath 설정 시 1회 계산해 캐싱한다.
    public bool IsMissing => _isMissing;

    public void RefreshFileStatus() =>
        _isMissing = !string.IsNullOrEmpty(_filePath) && !File.Exists(_filePath);

    public string StatusBadge => _isMissing ? " ⚠️ (파일 없음)" : (IsPathGuessed ? " 🔍 (추측된 경로)" : "");
    public string DisplayText => $"#{Key}: {FileName}{StatusBadge}";
}
