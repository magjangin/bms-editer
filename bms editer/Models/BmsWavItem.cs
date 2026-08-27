using System.IO;

namespace bms_editer.Models;

public sealed class BmsWavItem
{
    public string Key { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayText => $"#{Key}: {FileName}";
}
