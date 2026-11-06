using System;

// 테이블 원본은 Assets/Resources/Tables에만 두고, 이 클래스들은 직렬화 필드만 가진다.
// 조회는 TableManager, 검증·파생 값 계산은 GameTableRules가 담당한다.
// 파일명은 데이터 클래스명과 일치시켜야 한다(TableManager.Load가 그 이름으로 읽는다).

// POC 밸런스 수치와 해금 효과 목록. upgrades와 sequence는 템플릿 코드(UpgradeManager)가 쓴다.
[Serializable]
public sealed class GameplayTableData
{
    public string status, note;
    public SessionData session;
    public CoreData core;
    public ProductionData production;
    public SurvivorCombatData survivorCombat;
    public SurvivorData[] survivors = Array.Empty<SurvivorData>();
    public RecruitmentData recruitment;
    public ExperienceData experience;
    public InjuryData injury;
    public TreatmentData treatment;
    public BuildingsData buildings;
    public EnemyData[] enemies = Array.Empty<EnemyData>();
    public WaveData[] waves = Array.Empty<WaveData>();
    public ExpeditionRegionData[] expeditions = Array.Empty<ExpeditionRegionData>();
    public ExpeditionEventData expeditionEvents;
    public UpgradeSequenceData sequence;
    public UpgradeData[] upgrades = Array.Empty<UpgradeData>();
}

// 씬 진행 경로(타이틀 → 게임 → 재시작). TableManager.Load가 읽는 두 번째 테이블.
[Serializable]
public sealed class FlowTableData
{
    public string status, note;
    public SceneLinkData[] scenes = Array.Empty<SceneLinkData>();
}
