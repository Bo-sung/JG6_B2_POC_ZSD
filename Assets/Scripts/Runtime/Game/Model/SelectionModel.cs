using System;
using System.Collections.Generic;

// 현재 선택된 생존자 ID. 선택 가능 여부는 생성자에서 받은 판정을 따른다(사망·파견 제외).
public sealed class SelectionModel
{
    readonly List<string> ids = new List<string>();
    readonly Func<string, bool> canSelect;

    public IReadOnlyList<string> Ids => ids;
    public int Count => ids.Count;
    public event Action Changed;

    public SelectionModel(Func<string, bool> _canSelect)
    {
        canSelect = _canSelect ?? throw new ArgumentNullException(nameof(_canSelect));
    }

    public bool Contains(string _id) => ids.Contains(_id);

    public void Replace(IEnumerable<string> _ids)
    {
        ids.Clear();
        foreach (string id in _ids)
        {
            if (canSelect(id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        Changed?.Invoke();
    }

    public void Add(string _id)
    {
        if (canSelect(_id) && !ids.Contains(_id))
        {
            ids.Add(_id);
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        if (ids.Count > 0)
        {
            ids.Clear();
            Changed?.Invoke();
        }
    }

    // 사망하거나 파견된 인원을 선택에서 뺀다.
    public void Prune()
    {
        if (ids.RemoveAll(_id => !canSelect(_id)) > 0)
        {
            Changed?.Invoke();
        }
    }
}
