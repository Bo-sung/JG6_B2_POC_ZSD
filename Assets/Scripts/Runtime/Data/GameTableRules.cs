using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 검증과 파생 값 계산의 공통 구현. DTO와 로더에는 게임 규칙을 넣지 않는다.
// 규칙을 추가할 때: Check로 항목별 오류를 모은다(첫 오류에서 멈추지 않는다) → IsValid로보고 → 사용처는 RequireValid로 차단.
public sealed class GameTableRules
{
    public bool IsValid { get; private set; } = true;

    void Check(bool condition, string path, string rule)
    {
        if (condition) return;
        IsValid = false;
        Debug.LogError($"테이블 오류 [{path}]: {rule}");
    }

    // 키 인덱스 구성. 빈 키·중복 키·null 항목을 항목별로 보고한다.
    public Dictionary<string, T> Index<T>(T[] entries, Func<T, string> key, string path) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        Check(entries != null, path, "목록이 필요합니다.");
        foreach (var entry in entries ?? Array.Empty<T>())
        {
            string id = entry == null ? null : key(entry);
            Check(!string.IsNullOrEmpty(id), path, "항목과 비어 있지 않은 키가 필요합니다.");
            if (string.IsNullOrEmpty(id)) continue;
            Check(result.TryAdd(id, entry), path + "." + id, "키가 중복되었습니다.");
        }
        return result;
    }

    // 모든 숫자의 유한성과 누락된 중첩 설정을 재귀로 검사한다. DTO에 새 클래스를 추가하면 자동으로 덮인다.
    void Inspect(object value, string path)
    {
        if (value is string) return;
        if (value == null) { Check(false, path, "값이 누락되었습니다."); return; }
        if (value is float number) { Check(!float.IsNaN(number) && !float.IsInfinity(number), path, "유한한 숫자여야 합니다."); return; }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum) return;
        if (value is IEnumerable array)
        {
            int i = 0;
            foreach (var item in array) Inspect(item, path + "[" + i++ + "]");
            return;
        }
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            // 문자열은 선택 필드일 수 있다. 필수 키와 참조는 Reference에서 별도 검사한다.
            if (field.FieldType == typeof(string)) continue;
            Inspect(field.GetValue(value), path + "." + field.Name);
        }
    }

    void Positive(float value, string path) => Check(value > 0, path, "0보다 커야 합니다.");
    void Nonnegative(float value, string path) => Check(value >= 0, path, "0 이상이어야 합니다.");
    void Fraction(float value, string path) => Check(value >= 0 && value <= 1, path, "0~1이어야 합니다.");
    void IntRange(int value, int min, int max, string path) => Check(value >= min && value <= max, path, $"{min}~{max} 범위여야 합니다.");

    // 다른 테이블·같은 테이블의 키 참조. 빈 값이 허용되면 allowEmpty로 표현한다.
    void Reference(string id, string path, bool resolved, string label, bool allowEmpty = false)
    {
        if (string.IsNullOrEmpty(id)) { Check(allowEmpty, path, $"{label} 키가 필요합니다."); return; }
        Check(resolved, path, $"{label} '{id}'가 테이블에 없습니다.");
    }

    // 강화 단계처럼 1~maxLevel 항목이 하나씩 있어야 하는 목록 검사.
    public void ValidateLevelSet<T>(T[] entries, Func<T, int> getLevel, int maxLevel, string path) where T : class
    {
        Check(entries != null && entries.Length == maxLevel, path, $"단계 1~{maxLevel} 항목이 모두 필요합니다.");
        if (entries == null || maxLevel <= 0) return;
        var seen = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry == null) continue;
            int level = getLevel(entry);
            IntRange(level, 1, maxLevel, $"{path}.level({level})");
            Check(seen.Add(level), $"{path}.level({level})", "단계가 중복되었습니다.");
        }
    }

    public void Validate(TableManager tables)
    {
        Inspect(tables.Gameplay, nameof(GameplayTableData));
        Inspect(tables.Flow, nameof(FlowTableData));

        ValidatePlayer(tables.Gameplay.player);
        ValidateUpgrades(tables);
        ValidateScenes(tables);
    }

    void ValidatePlayer(PlayerData player)
    {
        if (player == null) { Check(false, "Gameplay.player", "플레이어 설정이 필요합니다."); return; }
        Positive(player.maxHealth, "Gameplay.player.maxHealth");
        Positive(player.moveSpeed, "Gameplay.player.moveSpeed");
        IntRange(player.dashCharges, 0, 10, "Gameplay.player.dashCharges");
        Nonnegative(player.dashCooldown, "Gameplay.player.dashCooldown");
        Positive(player.interactRange, "Gameplay.player.interactRange");
    }

    void ValidateUpgrades(TableManager tables)
    {
        var list = tables.Gameplay.upgrades ?? Array.Empty<UpgradeData>();
        Check(list.Length > 0, "GameplayTableData.upgrades", "해금 효과 목록이 비어 있습니다.");
        foreach (var upgrade in list)
        {
            if (upgrade == null || string.IsNullOrEmpty(upgrade.id)) continue;
            string path = "Gameplay.upgrades." + upgrade.id;
            Check(!string.IsNullOrEmpty(upgrade.displayName) || !string.IsNullOrEmpty(upgrade.id), path, "표시 이름이 필요합니다.");
            Check(!string.IsNullOrEmpty(upgrade.description), path + ".description", "설명이 비어 있으면 테이블 오류다(표기가 단일 출처이므로 코드에 폴백을 두지 않는다).");
            Check(TryCategory(upgrade.category, out _), path + ".category", $"EffectCategory 이름이어야 합니다: {upgrade.category}");
            Reference(upgrade.nextUpgrade, path + ".nextUpgrade", tables.TryGetUpgrade(upgrade.nextUpgrade, out _), "다음 해금", allowEmpty: true);
            Check(!string.Equals(upgrade.nextUpgrade, upgrade.id, StringComparison.Ordinal), path + ".nextUpgrade", "자기 자신을 다음 해금으로 연결했습니다.");
        }
        Reference(tables.FirstUpgradeId, "Gameplay.sequence.firstUpgrade", tables.TryGetUpgrade(tables.FirstUpgradeId, out _), "시작 해금");

        // 사슬이 순환하면 다음 해금을 영원히 계산하지 못한다.
        var walked = new HashSet<string>(StringComparer.Ordinal);
        for (string id = tables.FirstUpgradeId; !string.IsNullOrEmpty(id);)
        {
            if (!walked.Add(id)) { Check(false, "Gameplay.upgrades." + id + ".nextUpgrade", "해금 사슬에 순환이 있습니다."); break; }
            if (!tables.TryGetUpgrade(id, out var node)) break;
            id = node.nextUpgrade;
        }
    }

    void ValidateScenes(TableManager tables)
    {
        var list = tables.Flow.scenes ?? Array.Empty<SceneLinkData>();
        Check(list.Length > 0, "FlowTableData.scenes", "씬 진행 목록이 비어 있습니다.");
        foreach (var link in list)
        {
            if (link == null || string.IsNullOrEmpty(link.scene)) continue;
            string path = "Flow.scenes." + link.scene;
            Reference(link.nextScene, path + ".nextScene", tables.TryGetScene(link.nextScene, out _), "다음 씬", allowEmpty: true);
            Reference(link.restartScene, path + ".restartScene", tables.TryGetScene(link.restartScene, out _), "재시작 씬", allowEmpty: true);
            Check(!string.Equals(link.nextScene, link.scene, StringComparison.Ordinal), path + ".nextScene", "자기 자신을 다음 씬으로 연결했습니다.");
        }
    }

    // 문자열 키를 열거형으로 해석한다. 실패하면 false(호출 측이 Check로 보고한다).
    public static bool TryCategory(string key, out EffectCategory category)
    {
        category = EffectCategory.Utility;
        return !string.IsNullOrEmpty(key) && Enum.TryParse(key, out category);
    }

    public static EffectCategory Category(string key) => TryCategory(key, out var category) ? category : EffectCategory.Utility;
}
