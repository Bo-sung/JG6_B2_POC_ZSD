using System;

// 테이블 원본은 Assets/Resources/Tables에만 두고, 이 클래스들은 직렬화 필드만 가진다.
// 조회는 TableManager, 검증·파생 값 계산은 GameTableRules가 담당한다.
// 파일명은 데이터 클래스명과 일치시켜야 한다(TableManager.Load가 그 이름으로 읽는다).

// 플레이 공통 수치와 해금 효과 목록. 예시 필드이므로 프로젝트에서 필드를 더하거나 바꾼다.
[Serializable] public sealed class GameplayTableData
{
    public string status, note;
    public PlayerData player;
    public UpgradeSequenceData sequence;
    public UpgradeData[] upgrades = Array.Empty<UpgradeData>();
}

// 씬 진행 경로(타이틀 → 게임 → 재시작). TableManager.Load가 읽는 두 번째 테이블.
[Serializable] public sealed class FlowTableData
{
    public string status, note;
    public SceneLinkData[] scenes = Array.Empty<SceneLinkData>();
}
