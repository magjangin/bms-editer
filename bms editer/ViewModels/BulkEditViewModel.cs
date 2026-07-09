using System.Collections.Generic;
using bms_editer.Models;

namespace bms_editer.ViewModels;

// 선택된 노트를 일괄 수정하는 팝업창의 뼈대.
// 키음 일괄 변경 / N칸 이동 / 타입 일괄 변경은 추후 구현 예정.
public sealed class BulkEditViewModel
{
    public int SelectedCount { get; }

    public BulkEditViewModel(IReadOnlyList<BmsNote> selectedNotes)
    {
        SelectedCount = selectedNotes.Count;
    }
}
