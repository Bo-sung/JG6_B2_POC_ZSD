using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 검증과 파생 값 계산의 공통 구현. DTO와 로더에는 게임 규칙을 넣지 않는다.
// 규칙을 추가할 때: Check로 항목별 오류를 모은다(첫 오류에서 멈추지 않는다) → IsValid로보고 → 사용처는 RequireValid로 차단.
public sealed class GameTableRules
{
    // POC 시스템 크기. 수치 조정이 아닌 범위 변경이므로 테이블에 두지 않는다(POC_CORE.md 0절 6항).
    const int SURVIVOR_COUNT = 7;
    const int WAVE_COUNT = 3;
    const int EXPEDITION_REGION_COUNT = 2;
    const int STAT_MIN = 1;
    const int STAT_MAX = 5;

    // 특성 키. 표시 이름은 SurvivorLabels.Trait가 같은 키를 해석한다. 효과는 G4에서 구현한다.
    public static readonly string[] TRAIT_KEYS = { "marksman", "foreman", "scout" };

    public bool IsValid { get; private set; } = true;

    void Check(bool _condition, string _path, string _rule)
    {
        if (_condition)
        {
            return;
        }

        IsValid = false;
        Debug.LogError($"테이블 오류 [{_path}]: {_rule}");
    }

    // 키 인덱스 구성. 빈 키·중복 키·null 항목을 항목별로 보고한다.
    public Dictionary<string, T> Index<T>(T[] _entries, Func<T, string> _key, string _path) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        Check(_entries != null, _path, "목록이 필요합니다.");
        foreach (var entry in _entries ?? Array.Empty<T>())
        {
            string id = entry == null ? null : _key(entry);
            Check(!string.IsNullOrEmpty(id), _path, "항목과 비어 있지 않은 키가 필요합니다.");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            Check(result.TryAdd(id, entry), _path + "." + id, "키가 중복되었습니다.");
        }
        return result;
    }

    // 모든 숫자의 유한성과 누락된 중첩 설정을 재귀로 검사한다. DTO에 새 클래스를 추가하면 자동으로 덮인다.
    void Inspect(object _value, string _path)
    {
        if (_value is string)
        {
            return;
        }

        if (_value == null)
        {
            Check(false, _path, "값이 누락되었습니다.");
            return;
        }
        if (_value is float number)
        {
            Check(!float.IsNaN(number) && !float.IsInfinity(number), _path, "유한한 숫자여야 합니다.");
            return;
        }
        var type = _value.GetType();
        if (type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        if (_value is IEnumerable array)
        {
            int i = 0;
            foreach (var item in array)
            {
                Inspect(item, _path + "[" + i++ + "]");
            }

            return;
        }
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            // 문자열은 선택 필드일 수 있다. 필수 키와 참조는 Reference에서 별도 검사한다.
            if (field.FieldType == typeof(string))
            {
                continue;
            }

            Inspect(field.GetValue(_value), _path + "." + field.Name);
        }
    }

    void Positive(float _value, string _path) => Check(_value > 0, _path, "0보다 커야 합니다.");
    void Nonnegative(float _value, string _path) => Check(_value >= 0, _path, "0 이상이어야 합니다.");
    void Fraction(float _value, string _path) => Check(_value >= 0 && _value <= 1, _path, "0~1이어야 합니다.");
    void IntRange(int _value, int _min, int _max, string _path) => Check(_value >= _min && _value <= _max, _path, $"{_min}~{_max} 범위여야 합니다.");

    // 다른 테이블·같은 테이블의 키 참조. 빈 값이 허용되면 allowEmpty로 표현한다.
    void Reference(string _id, string _path, bool _resolved, string _label, bool _allowEmpty = false)
    {
        if (string.IsNullOrEmpty(_id))
        {
            Check(_allowEmpty, _path, $"{_label} 키가 필요합니다.");
            return;
        }
        Check(_resolved, _path, $"{_label} '{_id}'가 테이블에 없습니다.");
    }

    // 강화 단계처럼 1~maxLevel 항목이 하나씩 있어야 하는 목록 검사.
    public void ValidateLevelSet<T>(T[] _entries, Func<T, int> _getLevel, int _maxLevel, string _path) where T : class
    {
        Check(_entries != null && _entries.Length == _maxLevel, _path, $"단계 1~{_maxLevel} 항목이 모두 필요합니다.");
        if (_entries == null || _maxLevel <= 0)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var entry in _entries)
        {
            if (entry == null)
            {
                continue;
            }

            int level = _getLevel(entry);
            IntRange(level, 1, _maxLevel, $"{_path}.level({level})");
            Check(seen.Add(level), $"{_path}.level({level})", "단계가 중복되었습니다.");
        }
    }

    public void Validate(TableManager _tables)
    {
        Inspect(_tables.Gameplay, nameof(GameplayTableData));
        Inspect(_tables.Flow, nameof(FlowTableData));

        ValidateSession(_tables.Gameplay);
        ValidateSurvivors(_tables.Gameplay);
        ValidateRecruitment(_tables.Gameplay);
        ValidateBuildings(_tables.Gameplay.buildings);
        ValidateWaves(_tables.Gameplay);
        ValidateExpeditions(_tables.Gameplay);
        ValidateUpgrades(_tables);
        ValidateScenes(_tables);
    }

    void ValidateSession(GameplayTableData _gameplay)
    {
        if (_gameplay.session == null || _gameplay.core == null || _gameplay.production == null)
        {
            Check(false, "Gameplay", "session, core, production 설정이 필요합니다.");
            return;
        }
        Positive(_gameplay.session.prepSeconds, "Gameplay.session.prepSeconds");
        Positive(_gameplay.session.alertSeconds, "Gameplay.session.alertSeconds");
        Positive(_gameplay.session.combatSeconds, "Gameplay.session.combatSeconds");
        Nonnegative(_gameplay.session.timeoutCoreDamagePerEnemy, "Gameplay.session.timeoutCoreDamagePerEnemy");
        Nonnegative(_gameplay.session.startScrap, "Gameplay.session.startScrap");
        ValidateSpeedOptions(_gameplay.session.speedOptions);
        Positive(_gameplay.core.maxHp, "Gameplay.core.maxHp");
        Positive(_gameplay.production.maxWorkers, "Gameplay.production.maxWorkers");
        Positive(_gameplay.production.scrapPerSecondPerWork, "Gameplay.production.scrapPerSecondPerWork");
    }

    public void ValidateSpeedOptions(float[] _options)
    {
        Check(_options != null && Array.IndexOf(_options, 1f) >= 0, "Gameplay.session.speedOptions", "1배를 포함해야 합니다.");
        var seen = new HashSet<float>();
        foreach (float option in _options ?? Array.Empty<float>())
        {
            Check(!float.IsNaN(option) && !float.IsInfinity(option) && option > 0 && seen.Add(option),
                "Gameplay.session.speedOptions", "서로 다른 유한한 양수여야 합니다.");
        }
    }

    // 지상·탑 사거리 표시와 실제 교전이 같은 순수 계산을 쓴다.
    public static float SurvivorRange(SurvivorCombatData _combat, int _combatStat, float _towerBonus = 0)
    {
        return _combat.attackRange + _combat.attackRangePerCombat * _combatStat + _towerBonus;
    }

    void ValidateSurvivors(GameplayTableData _gameplay)
    {
        var combat = _gameplay.survivorCombat;
        if (combat == null)
        {
            Check(false, "Gameplay.survivorCombat", "생존자 전투 설정이 필요합니다.");
        }
        else
        {
            Positive(combat.moveSpeed, "Gameplay.survivorCombat.moveSpeed");
            Positive(combat.attackRange, "Gameplay.survivorCombat.attackRange");
            Nonnegative(combat.attackRangePerCombat, "Gameplay.survivorCombat.attackRangePerCombat");
            Positive(combat.attackInterval, "Gameplay.survivorCombat.attackInterval");
            Positive(combat.baseDamage, "Gameplay.survivorCombat.baseDamage");
            Nonnegative(combat.damagePerCombat, "Gameplay.survivorCombat.damagePerCombat");
            Nonnegative(combat.towerRangeBonus, "Gameplay.survivorCombat.towerRangeBonus");
            Positive(combat.towerDamageMultiplier, "Gameplay.survivorCombat.towerDamageMultiplier");
        }

        var list = _gameplay.survivors ?? Array.Empty<SurvivorData>();
        Check(list.Length == SURVIVOR_COUNT, "Gameplay.survivors", $"생존자는 {SURVIVOR_COUNT}명이어야 합니다.");
        Index(list, _entry => _entry.id, "Gameplay.survivors");
        foreach (var survivor in list)
        {
            if (survivor == null || string.IsNullOrEmpty(survivor.id))
            {
                continue;
            }

            string path = "Gameplay.survivors." + survivor.id;
            Check(!string.IsNullOrEmpty(survivor.displayName), path + ".displayName", "이름이 필요합니다.");
            IntRange(survivor.combat, STAT_MIN, STAT_MAX, path + ".combat");
            IntRange(survivor.work, STAT_MIN, STAT_MAX, path + ".work");
            IntRange(survivor.exploration, STAT_MIN, STAT_MAX, path + ".exploration");
            Positive(survivor.maxHp, path + ".maxHp");
        }
    }

    // 인력 수급. 후보는 방문 몫과 구조 몫에 쓰이므로 방문 수 이상이 필요하고(V2-IMPL-09),
    // 초기 인원은 capacity 안에 들어와야 한다. 방문 웨이브가 현재 웨이브 수를 넘는 것은
    // G5(6웨이브)까지 열려 있는 값이므로 여기서 막지 않는다.
    void ValidateRecruitment(GameplayTableData _gameplay)
    {
        var data = _gameplay.recruitment;
        if (data == null)
        {
            Check(false, "Gameplay.recruitment", "recruitment 설정이 필요합니다.");
            return;
        }
        Check(data.capacity >= (_gameplay.survivors ?? Array.Empty<SurvivorData>()).Length, "Gameplay.recruitment.capacity", "초기 인원 이상이어야 합니다.");
        Check(data.capacity >= 1, "Gameplay.recruitment.capacity", "1 이상이어야 합니다.");
        Nonnegative(data.visitorCost, "Gameplay.recruitment.visitorCost");
        var waves = data.visitorWaves ?? Array.Empty<int>();
        Check(waves.Length > 0, "Gameplay.recruitment.visitorWaves", "방문 웨이브가 필요합니다.");
        var seenWaves = new HashSet<int>();
        foreach (int wave in waves)
        {
            Check(wave >= 1, "Gameplay.recruitment.visitorWaves", "웨이브 번호는 1 이상이어야 합니다.");
            Check(seenWaves.Add(wave), "Gameplay.recruitment.visitorWaves", "방문 웨이브가 중복되었습니다.");
        }

        var candidates = data.candidates ?? Array.Empty<SurvivorData>();
        Check(candidates.Length >= waves.Length, "Gameplay.recruitment.candidates", "후보 수는 방문 수 이상이어야 합니다.");
        Index(candidates, _entry => _entry.id, "Gameplay.recruitment.candidates");
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var survivor in _gameplay.survivors ?? Array.Empty<SurvivorData>())
        {
            // 초기 인원과 후보가 같은 이름을 쓰면 목록에서 구별할 수 없다.
            if (survivor != null && !string.IsNullOrEmpty(survivor.displayName))
            {
                seenNames.Add(survivor.displayName);
            }
        }
        foreach (var candidate in candidates)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.id))
            {
                continue;
            }

            string path = "Gameplay.recruitment.candidates." + candidate.id;
            Check(!string.IsNullOrEmpty(candidate.displayName), path + ".displayName", "이름이 필요합니다.");
            Check(seenNames.Add(candidate.displayName), path + ".displayName", "이름이 중복되었습니다.");
            IntRange(candidate.combat, STAT_MIN, STAT_MAX, path + ".combat");
            IntRange(candidate.work, STAT_MIN, STAT_MAX, path + ".work");
            IntRange(candidate.exploration, STAT_MIN, STAT_MAX, path + ".exploration");
            Positive(candidate.maxHp, path + ".maxHp");
            Check(IsKnownTrait(candidate.trait), path + ".trait", $"특성 키는 비어 있거나 {string.Join(", ", TRAIT_KEYS)} 중 하나여야 합니다: {candidate.trait}");
        }
    }

    // 초기 인원의 특성도 같은 키 집합으로 검사한다. 효과는 G4에서 붙는다.
    static bool IsKnownTrait(string _trait) => string.IsNullOrEmpty(_trait) || Array.IndexOf(TRAIT_KEYS, _trait) >= 0;

    void ValidateBuildings(BuildingsData _buildings)
    {
        if (_buildings == null || _buildings.wall == null || _buildings.tower == null)
        {
            Check(false, "Gameplay.buildings", "방벽과 감시탑 설정이 필요합니다.");
            return;
        }
        Positive(_buildings.gridSize, "Gameplay.buildings.gridSize");
        Positive(_buildings.wall.cost, "Gameplay.buildings.wall.cost");
        Positive(_buildings.wall.maxHp, "Gameplay.buildings.wall.maxHp");
        Positive(_buildings.tower.cost, "Gameplay.buildings.tower.cost");
        Positive(_buildings.tower.maxHp, "Gameplay.buildings.tower.maxHp");
    }

    void ValidateWaves(GameplayTableData _gameplay)
    {
        var enemies = Index(_gameplay.enemies, _entry => _entry.id, "Gameplay.enemies");
        foreach (var enemy in enemies.Values)
        {
            string path = "Gameplay.enemies." + enemy.id;
            Positive(enemy.maxHp, path + ".maxHp");
            Positive(enemy.moveSpeed, path + ".moveSpeed");
            Positive(enemy.attackRange, path + ".attackRange");
            Positive(enemy.attackInterval, path + ".attackInterval");
            Positive(enemy.damage, path + ".damage");
            Positive(enemy.buildingDamageMultiplier, path + ".buildingDamageMultiplier");
            Nonnegative(enemy.aggroRange, path + ".aggroRange");
        }

        var waves = _gameplay.waves ?? Array.Empty<WaveData>();
        Check(waves.Length == WAVE_COUNT, "Gameplay.waves", $"웨이브는 {WAVE_COUNT}개여야 합니다.");
        for (int i = 0; i < waves.Length; i++)
        {
            string path = $"Gameplay.waves[{i}]";
            if (waves[i] == null)
            {
                continue;
            }

            Nonnegative(waves[i].spawnInterval, path + ".spawnInterval");
            var spawns = waves[i].spawns ?? Array.Empty<WaveSpawnData>();
            Check(spawns.Length > 0, path + ".spawns", "스폰이 비어 있습니다.");
            foreach (var spawn in spawns)
            {
                if (spawn == null)
                {
                    continue;
                }

                Reference(spawn.enemyId, path + ".enemyId", enemies.ContainsKey(spawn.enemyId ?? string.Empty), "적");
                Check(spawn.lane == "west" || spawn.lane == "east", path + ".lane", "west 또는 east여야 합니다.");
                Positive(spawn.count, path + ".count");
            }
        }
    }

    void ValidateExpeditions(GameplayTableData _gameplay)
    {
        var regions = Index(_gameplay.expeditions, _entry => _entry.id, "Gameplay.expeditions");
        Check(regions.Count == EXPEDITION_REGION_COUNT, "Gameplay.expeditions", $"탐사 지역은 {EXPEDITION_REGION_COUNT}곳이어야 합니다.");
        foreach (var region in regions.Values)
        {
            string path = "Gameplay.expeditions." + region.id;
            Check(!string.IsNullOrEmpty(region.displayName), path + ".displayName", "이름이 필요합니다.");
            Positive(region.durationSeconds, path + ".durationSeconds");
            Check(region.minParty >= 1 && region.minParty <= region.maxParty, path + ".minParty", "1 이상이고 maxParty 이하여야 합니다.");
            Nonnegative(region.rewardBase, path + ".rewardBase");
            Nonnegative(region.rewardPerExploration, path + ".rewardPerExploration");
            Fraction(region.rewardChanceBase, path + ".rewardChanceBase");
            Fraction(region.deathChanceBase, path + ".deathChanceBase");
        }
    }

    void ValidateUpgrades(TableManager _tables)
    {
        var list = _tables.Gameplay.upgrades ?? Array.Empty<UpgradeData>();
        Check(list.Length > 0, "GameplayTableData.upgrades", "해금 효과 목록이 비어 있습니다.");
        foreach (var upgrade in list)
        {
            if (upgrade == null || string.IsNullOrEmpty(upgrade.id))
            {
                continue;
            }

            string path = "Gameplay.upgrades." + upgrade.id;
            Check(!string.IsNullOrEmpty(upgrade.displayName) || !string.IsNullOrEmpty(upgrade.id), path, "표시 이름이 필요합니다.");
            Check(!string.IsNullOrEmpty(upgrade.description), path + ".description", "설명이 비어 있으면 테이블 오류다(표기가 단일 출처이므로 코드에 폴백을 두지 않는다).");
            Check(TryCategory(upgrade.category, out _), path + ".category", $"EffectCategory 이름이어야 합니다: {upgrade.category}");
            Reference(upgrade.nextUpgrade, path + ".nextUpgrade", _tables.TryGetUpgrade(upgrade.nextUpgrade, out _), "다음 해금", _allowEmpty: true);
            Check(!string.Equals(upgrade.nextUpgrade, upgrade.id, StringComparison.Ordinal), path + ".nextUpgrade", "자기 자신을 다음 해금으로 연결했습니다.");
        }
        Reference(_tables.FirstUpgradeId, "Gameplay.sequence.firstUpgrade", _tables.TryGetUpgrade(_tables.FirstUpgradeId, out _), "시작 해금");

        // 사슬이 순환하면 다음 해금을 영원히 계산하지 못한다.
        var walked = new HashSet<string>(StringComparer.Ordinal);
        for (string id = _tables.FirstUpgradeId; !string.IsNullOrEmpty(id);)
        {
            if (!walked.Add(id))
            {
                Check(false, "Gameplay.upgrades." + id + ".nextUpgrade", "해금 사슬에 순환이 있습니다.");
                break;
            }
            if (!_tables.TryGetUpgrade(id, out var node))
            {
                break;
            }

            id = node.nextUpgrade;
        }
    }

    void ValidateScenes(TableManager _tables)
    {
        var list = _tables.Flow.scenes ?? Array.Empty<SceneLinkData>();
        Check(list.Length > 0, "FlowTableData.scenes", "씬 진행 목록이 비어 있습니다.");
        foreach (var link in list)
        {
            if (link == null || string.IsNullOrEmpty(link.scene))
            {
                continue;
            }

            string path = "Flow.scenes." + link.scene;
            Reference(link.nextScene, path + ".nextScene", _tables.TryGetScene(link.nextScene, out _), "다음 씬", _allowEmpty: true);
            Reference(link.restartScene, path + ".restartScene", _tables.TryGetScene(link.restartScene, out _), "재시작 씬", _allowEmpty: true);
            Check(!string.Equals(link.nextScene, link.scene, StringComparison.Ordinal), path + ".nextScene", "자기 자신을 다음 씬으로 연결했습니다.");
        }
    }

    // 문자열 키를 열거형으로 해석한다. 실패하면 false(호출 측이 Check로 보고한다).
    public static bool TryCategory(string _key, out EffectCategory _category)
    {
        _category = EffectCategory.Utility;
        return !string.IsNullOrEmpty(_key) && Enum.TryParse(_key, out _category);
    }

    public static EffectCategory Category(string _key) => TryCategory(_key, out var category) ? category : EffectCategory.Utility;
}
