using System;
using System.Collections.Generic;
using UnityEngine;

// 씬 로컬 해금 상태 보관소. 테이블에는 상태를 두지 않고 이 컴포넌트가 보유한다.
// 사용:
//   Bind(owner)           .effects가 적용될 대상(플레이어 루트)을 정한다.
//   Register(effect)       IUpgrade 구현체를 등록한다. 이미 해금된 효과면 즉시 Apply.
//   Grant(id)              테이블 id로 해금하고 효과를 적용한다. 다음 해금 id를 갱신한다.
//   Capture() / Restore()  현재 해금 집합을 저장·복원한다(씬 재시작 시 입장 시점으로 되돌리기용).
[DisallowMultipleComponent]
public sealed class UpgradeManager : MonoBehaviour
{
    TableManager Tables => TableManager.Instance.RequireValid();
    readonly HashSet<string> unlocked = new(StringComparer.Ordinal);
    readonly List<IUpgrade> effects = new();
    GameObject owner;
    string nextUpgrade;

    public event Action<UpgradeData> Granted;
    public string NextUpgradeId => nextUpgrade;
    public IEnumerable<string> UnlockedIds => unlocked;

    public void Bind(GameObject _target)
    {
        owner = _target;
        Reapply();
    }

    public void Register(IUpgrade _effect)
    {
        if (_effect == null || effects.Contains(_effect))
        {
            return;
        }

        effects.Add(_effect);
        if (IsUnlocked(_effect.Id))
        {
            _effect.Apply(owner);
        }
    }

    public bool IsUnlocked(string _id) => !string.IsNullOrEmpty(_id) && unlocked.Contains(_id);

    // 이미 해금된 id를 다시 요청하면 false. 해금 시 effects.Apply → Granted 순으로 알린다.
    public bool Grant(string _id)
    {
        var upgrade = Tables.Upgrade(_id);          // 모르는 id는 예외로 막는다.
        if (unlocked.Contains(upgrade.id))
        {
            return false;
        }

        unlocked.Add(upgrade.id);
        RefreshNextUpgrade();
        foreach (var effect in effects)
        {
            if (effect.Id == upgrade.id)
            {
                effect.Apply(owner);
            }
        }

        Debug.Log($"해금: {Presentation.Name(upgrade)} ({upgrade.id})", this);
        Granted?.Invoke(upgrade);
        GameEvents.RaiseUpgradeGranted(upgrade);
        return true;
    }

    // 사슬의 다음 해금을 해금한다. 목록이 소진되면 false.
    public bool GrantNext()
    {
        if (string.IsNullOrEmpty(nextUpgrade))
        {
            RefreshNextUpgrade();
        }

        return !string.IsNullOrEmpty(nextUpgrade) && Grant(nextUpgrade);
    }

    public string[] Capture()
    {
        var result = new string[unlocked.Count];
        unlocked.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);   // 배열 순서에 의존하지 않는 결정적 스냅샷
        return result;
    }

    // null이면 초기 상태. id 집합을 그대로 적용하고 효과를 다시 붙인다.
    public void Restore(string[] _snapshot)
    {
        foreach (var effect in effects)
        {
            effect.Remove(owner);
        }

        unlocked.Clear();
        foreach (string id in _snapshot ?? Array.Empty<string>())
        {
            unlocked.Add(Tables.Upgrade(id).id);      // 모르는 id는 예외로 막는다.
        }

        RefreshNextUpgrade();
        Reapply();
    }

    void Reapply()
    {
        foreach (var effect in effects)
        {
            effect.Remove(owner);
        }

        foreach (var effect in effects)
        {
            if (IsUnlocked(effect.Id))
            {
                effect.Apply(owner);
            }
        }
    }

    // nextUpgrade는 사슬에서 아직 해금되지 않은 첫 항목. 키로 따라가므로 배열 순서를 보지 않는다.
    void RefreshNextUpgrade()
    {
        nextUpgrade = Tables.FirstUpgradeId;
        int guard = 0;
        while (!string.IsNullOrEmpty(nextUpgrade) && IsUnlocked(nextUpgrade) && guard++ < 1000)
        {
            nextUpgrade = Tables.TryGetUpgrade(nextUpgrade, out var node) ? node.nextUpgrade : null;
        }
    }
}
