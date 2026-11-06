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
    public float[] speedOptions;
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
    public float attackRangePerCombat;
    public float attackInterval;
    public float baseDamage;
    public float damagePerCombat;
    public float towerRangeBonus;
    public float towerDamageMultiplier;
}

// 생존자 한 명(초기 인원과 영입 후보 공용). id가 키이며 능력치는 1~5.
// trait은 G4에서 효과를 가지는 키이고, 비어 있으면 특성이 없다.
[Serializable]
public sealed class SurvivorData
{
    public string id, displayName;
    public int combat, work, exploration;
    public float maxHp;
    public string trait;
}

// 인력 수급(거점 방문과 구조 몫). capacity는 파견·중상을 포함한 살아 있는 인원의 상한이다.
[Serializable]
public sealed class RecruitmentData
{
    public int capacity;
    public int visitorCost;
    public int[] visitorWaves = Array.Empty<int>();
    public SurvivorData[] candidates = Array.Empty<SurvivorData>();
}

// 경험치와 성장 수치(TD-40~44, V2-IMPL-07, 14).
[Serializable]
public sealed class ExperienceData
{
    public int xpPerStat;
    public int maxStat;
    public int combatFirstHitXp;
    public int combatKillParticipationXp;
    public int combatWaveCap;
    public float workSecondsPerXp;
    public int treatmentXp;
    public int workWaveCap;
    public int expeditionReturnXp;
    public int expeditionEventResponseBonus;
    public int expeditionEncounterSurvivalBonus;
    public int expeditionCap;
    public float withdrawMultiplier;
    public float traitMultiplier;
}

// 부상 구간(최대 HP 대비 비율). normalMinHpFraction 이상 정상, lightMinHpFraction 이상 경상, 그 아래는 중상, 0은 사망이다.
// lightEfficiency는 경상의 전투·작업·탐사 효율이다(V2-IMPL-07).
[Serializable]
public sealed class InjuryData
{
    public float normalMinHpFraction;
    public float lightMinHpFraction;
    public float lightEfficiency;
}

// 치료 1회. 소요 시간 = baseSeconds ÷ ((1 + speedPerWork × (Work - referenceWork)) × 담당자 효율).
// healFraction은 최대 HP 대비 회복량이다.
[Serializable]
public sealed class TreatmentData
{
    public int cost;
    public float healFraction;
    public float baseSeconds;
    public float referenceWork;
    public float speedPerWork;
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
    // 사건 발생 확률과 유형 가중치(TD-31). 가중치 합으로 나누어 쓴다.
    public float eventChance, rescueWeight, supplyWeight, encounterWeight;
}

// 탐사 사건의 공통 수치. 사건은 소요 시간의 triggerMinFraction~triggerMaxFraction 사이에 도착하고,
// timeoutSeconds 뒤 '임무 계속'으로 끝난다. 철수하면 남은 시간이 withdrawSeconds 이하가 된다(TD-34).
[Serializable]
public sealed class ExpeditionEventData
{
    public float triggerMinFraction, triggerMaxFraction;
    public float timeoutSeconds;
    public float withdrawSeconds;
    // 파티에 scout이 있을 때 사망·피해 위험에 곱한다. 여럿이어도 중첩하지 않는다(TD-44).
    public float scoutRiskMultiplier;
    public RescueEventData rescue;
    public SupplyEventData supply;
    public EncounterEventData encounter;
}

// 성공률 = chanceBase + chancePerExploration × 파티 실효 탐사 합, 상한 chanceMax. 실패하면 1명이 최대 HP × failureDamageFraction 피해(TD-30).
[Serializable]
public sealed class RescueEventData
{
    public float extraSeconds;
    public float chanceBase, chancePerExploration, chanceMax;
    public float failureDamageFraction;
}

// 추가 보상 = 기본 보상 × bonusRewardFraction(올림). damageChance 확률로 1명이 최대 HP × damageFraction 피해(TD-32).
[Serializable]
public sealed class SupplyEventData
{
    public float extraSeconds;
    public float bonusRewardFraction;
    public float damageChance, damageFraction;
}

// 피해 비율 = damageMaxFraction - damageReductionPerCombat × 파티 실효 전투 합, 하한 damageMinFraction(TD-33).
[Serializable]
public sealed class EncounterEventData
{
    public float delaySeconds;
    public float damageMinFraction, damageMaxFraction, damageReductionPerCombat;
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
