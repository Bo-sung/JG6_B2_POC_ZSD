using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 전역 테이블의 유일한 보관소. 씬 오브젝트나 실행 중 상태(GameObject·풀·씬 진행 상태)는 여기에 두지 않는다.
// 사용 규칙:
//   초기화    TableManager.Initialize()                       // 기동 중 한 번, 없어도 지연 로드된다
//   조회      TableManager.Instance.Upgrade(id)               // 모르는 키면 예외
//   안전하게  TableManager.Instance.TryGetScene(name, out ..)  // 없으면 false
//   차단      TableManager.Instance.RequireValid()            // 잘못된 테이블로 동작하지 않게 초기화 거부
public sealed class TableManager
{
    static TableManager instance;
    public static TableManager Instance => instance ??= new TableManager();
    const string ResourceFolder = "Tables/";

    public GameplayTableData Gameplay { get; }
    public FlowTableData Flow { get; }
    public bool IsValid { get; }

    readonly Dictionary<string, UpgradeData> upgrades;
    readonly Dictionary<string, SceneLinkData> scenes;

    TableManager()
    {
        bool loaded = true;
        Gameplay = Load<GameplayTableData>(ref loaded);
        Flow = Load<FlowTableData>(ref loaded);

        var rules = new GameTableRules();
        upgrades = rules.Index(Gameplay.upgrades, e => e.id, "GameplayTableData.upgrades");
        scenes = rules.Index(Flow.scenes, e => e.scene, "FlowTableData.scenes");
        // 노출용 목록은 배열 순서에 의존하지 않는다. 사슬의 첫 항목 우선, 이후 id 오름차순의 결정적 순서.
        string first = Gameplay.sequence == null ? null : Gameplay.sequence.firstUpgrade;
        Upgrades = (Gameplay.upgrades ?? Array.Empty<UpgradeData>())
            .Where(entry => entry != null && !string.IsNullOrEmpty(entry.id))
            .OrderBy(entry => entry.id == first ? 0 : 1)
            .ThenBy(entry => entry.id, StringComparer.Ordinal)
            .ToArray();

        rules.Validate(this);
        IsValid = loaded && rules.IsValid;
    }

    // 사슬의 첫 해금 id. 없으면 빈 문자열.
    public string FirstUpgradeId => Gameplay.sequence == null ? string.Empty : Gameplay.sequence.firstUpgrade ?? string.Empty;
    // UI 등에 노출하는 해금 목록.
    public IReadOnlyList<UpgradeData> Upgrades { get; }
    public IReadOnlyList<SceneLinkData> Scenes => Flow.scenes ?? Array.Empty<SceneLinkData>();

    public static TableManager Initialize() => Instance;

    // 오류를 모두 보고한 뒤, 필수 데이터를 사용하는 기능만 명시적으로 초기화를 거부한다.
    public TableManager RequireValid()
    {
        if (!IsValid) throw new InvalidOperationException("게임 테이블이 올바르지 않습니다. 먼저 출력된 테이블 오류를 수정하세요.");
        return this;
    }

    public UpgradeData Upgrade(string id) => Find(upgrades, id, "해금");
    public SceneLinkData Scene(string scene) => Find(scenes, scene, "씬 진행");
    public bool TryGetScene(string scene, out SceneLinkData link)
    {
        if (!string.IsNullOrEmpty(scene) && scenes.TryGetValue(scene, out link)) return true;
        link = null;
        return false;
    }
    public bool TryGetUpgrade(string id, out UpgradeData upgrade)
    {
        if (!string.IsNullOrEmpty(id) && upgrades.TryGetValue(id, out upgrade)) return true;
        upgrade = null;
        return false;
    }

    // nextScene 미참조 등 진행 경로를 따라갈 때 쓰는 빈 값 안전 조회.
    public string NextSceneOf(string scene) => TryGetScene(scene, out var link) ? link.nextScene ?? string.Empty : string.Empty;
    public string RestartSceneOf(string scene) => TryGetScene(scene, out var link)
        ? string.IsNullOrEmpty(link.restartScene) ? link.scene : link.restartScene
        : scene;

    // 모르는 키면 기본값으로 조용히 동작하지 않고 예외로 막는다.
    static T Find<T>(Dictionary<string, T> index, string id, string table) where T : class
    {
        if (!string.IsNullOrEmpty(id) && index.TryGetValue(id, out var value)) return value;
        throw new KeyNotFoundException($"{table} 테이블에 '{id}' 키가 없습니다.");
    }

    static T Load<T>(ref bool valid) where T : class, new()
    {
        string path = ResourceFolder + typeof(T).Name;
        var asset = Resources.Load<TextAsset>(path);
        if (!asset)
        {
            Debug.LogError($"테이블 누락: Resources/{path}.json");
            valid = false;
            return new T();
        }
        try
        {
            string json = asset.text;
#if UNITY_EDITOR
            // 편집 도구의 연속 실행도 에셋 자동 새로고침 전 최신 원본을 읽는다.
            string diskPath = EditorAssetPath<T>();
            if (System.IO.File.Exists(diskPath)) json = System.IO.File.ReadAllText(diskPath);
#endif
            var table = JsonUtility.FromJson<T>(json);
            if (table != null) return table;
            Debug.LogError($"테이블 내용이 null입니다: {path}");
        }
        catch (Exception error) when (error is ArgumentException || error is System.IO.IOException || error is UnauthorizedAccessException)
        {
            Debug.LogError($"테이블 읽기 실패: {path}: {error.Message}");
        }
        valid = false;
        return new T();
    }

    // 기존 데이터를 수정하는 테스트는 전후에 호출해 다음 테스트로 변경을 전파하지 않는다.
    public static void Reload() => instance = null;

#if UNITY_EDITOR
    public static string EditorAssetPath<T>() => "Assets/Resources/" + ResourceFolder + typeof(T).Name + ".json";
    public static void ReloadAfterTableEdit() { Reload(); Initialize(); }
#endif

    // Enter Play Mode Options로 도메인 리로드를 꺼도 정적 상태는 매 실행 초기화된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => instance = null;
}
