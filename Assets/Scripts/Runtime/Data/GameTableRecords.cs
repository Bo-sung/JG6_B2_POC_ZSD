using System;

// 순수 직렬화 항목. 수치는 JSON에 두고 이 파일에는 필드만 둔다.
// 검증 규칙이나 조회 로직을 여기 넣지 않는다(GameTableRules / TableManager).

// 단계 시간(초)과 세션 시작값.
[Serializable]
public sealed class SessionData
{
    public float prepSeconds;
    public float alertSeconds;
    public float combatSeconds;
    public float timeoutCoreDamagePerEnemy;
    public int startScrap;
    public int defaultSeed;
}

[Serializable]
public sealed class CoreData
{
    public float maxHp;
}

// 작업 능력 1당 초당 스크랩 생산량. 정원은 생산 시설 전체 기준.
[Serializable]
public sealed class ProductionData
{
    public int maxWorkers;
    public float scrapPerSecondPerWork;
}

// 모든 생존자에게 공통인 전투 수치. 사격 피해 = baseDamage + damagePerCombat × Combat.
[Serializable]
public sealed class SurvivorCombatData
{
    public float moveSpeed;
    public float attackRange;
    public float attackInterval;
    public float baseDamage;
    public float damagePerCombat;
    public float towerRangeBonus;
    public float towerDamageMultiplier;
}

// 초기 생존자 한 명. id가 키이며 능력치는 1~5.
[Serializable]
public sealed class SurvivorData
{
    public string id, displayName;
    public int combat, work, exploration;
    public float maxHp;
}

[Serializable]
public sealed class BuildingStatData
{
    public int cost;
    public float maxHp;
}

[Serializable]
public sealed class BuildingsData
{
    public float gridSize;
    public BuildingStatData wall;
    public BuildingStatData tower;
}

// 적 유형. buildingDamageMultiplier는 방벽·감시탑에 줄 때 곱한다.
[Serializable]
public sealed class EnemyData
{
    public string id, displayName;
    public float maxHp, moveSpeed, attackRange, attackInterval, damage, buildingDamageMultiplier, aggroRange;
}

// 한 진입로의 스폰 한 묶음. lane은 "west" 또는 "east".
[Serializable]
public sealed class WaveSpawnData
{
    public string enemyId, lane;
    public int count;
}

[Serializable]
public sealed class WaveData
{
    public float spawnInterval;
    public WaveSpawnData[] spawns = Array.Empty<WaveSpawnData>();
}

// 탐사 지역. 확률은 0~1로 잘라 쓴다.
//   보상 확률 = rewardChanceBase + rewardChancePerExploration × 파티 탐사 합
//   보상량 = rewardBase + rewardPerExploration × 파티 탐사 합 (반올림)
//   사망 확률 = deathChanceBase + deathChancePerExploration × 파티 탐사 합 + deathChancePerExtraMember × (인원 - 1)
[Serializable]
public sealed class ExpeditionRegionData
{
    public string id, displayName, riskLabel, rewardLabel;
    public float durationSeconds;
    public int minParty, maxParty;
    public float rewardChanceBase, rewardChancePerExploration, rewardBase, rewardPerExploration;
    public float deathChanceBase, deathChancePerExploration, deathChancePerExtraMember;
}

// 해금(확장) 효과 한 항목. id로 찾고 nextUpgrade로 다음 해금을 잇는다.
// category는 Presentation이 색·제목의 기준으로 읽는 키이며 EffectCategory 이름과 일치해야 한다.
[Serializable]
public sealed class UpgradeData
{
    public string id, nextUpgrade, displayName, description, category;
}

// 해금 사슬의 시작. 배열 순서에 의존하지 않기 위한 명시적 시작 키.
[Serializable]
public sealed class UpgradeSequenceData
{
    public string firstUpgrade;
}

// 씬 진행 경로 한 행. 씬 이름이 키이며 배열 순서는 의미하지 않는다.
// nextScene이 비어 있으면 최종 씬, restartScene은 사망·재시작 때 로드하는 씬.
[Serializable]
public sealed class SceneLinkData
{
    public string scene, nextScene, restartScene;
}
