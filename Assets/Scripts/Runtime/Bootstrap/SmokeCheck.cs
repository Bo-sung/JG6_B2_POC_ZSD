#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UnityEngine;

// 개발 전용 스모크 검사. 실행 인자에 -projectSmoke 가 있을 때만 동작하고, 평범한 플레이 중에는 오브젝트를 추가하지 않는다.
// 통과하면 MYJG_SMOKE_PASS, 실패하면 MYJG_SMOKE_FAIL을 남기고 종료 코드로 결과를 알린다(CI 판정용).
//   에디터 CLI: Unity.exe -batchMode -projectSmoke -executeMethod ... 또는 Play Mode에서 인자만으로 동작
public sealed class SmokeCheck : MonoBehaviour
{
    public const string COMMAND_LINE_FLAG = "-projectSmoke";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Array.Exists(Environment.GetCommandLineArgs(), _arg => _arg == COMMAND_LINE_FLAG))
        {
            return;
        }

        var host = new GameObject("Development smoke check");
        DontDestroyOnLoad(host);
        host.AddComponent<SmokeCheck>();
    }

    static void Require(bool _condition, string _message)
    {
        if (_condition)
        {
            return;
        }

        Debug.LogError("MYJG_SMOKE_FAIL: " + _message);
        Application.Quit(2);
        throw new InvalidOperationException(_message);
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return null;
        yield return null;

        // 1) 테이블: 원본 로드와 검증
        var tables = TableManager.Instance;
        Require(tables.IsValid, "게임 테이블 검증이 실패했습니다. 콘솔의 테이블 오류를 수정하세요.");
        Require(tables.Upgrades.Count > 0, "해금 목록이 비어 있습니다.");
        Require(tables.Scenes.Count > 0, "씬 진행 목록이 비어 있습니다.");

        // 2) 씬 로컬 풀: 등록 → 대여 → 반납 왕복
        var pool = ScenePool.Current;
        Require(pool != null, "ScenePool을 씬에서 만들지 못했습니다.");
        var template = new GameObject("Smoke pooled object");
        var first = pool.Rent("smoke.unregistered.template", template.transform.position, Quaternion.identity);   // 미등록 key는 null이어야 한다
        Require(first == null, "미등록 key의 대여가 오류 대신 오브젝트를 반환했습니다.");
        pool.Register("smoke", template, 1);
        var rented = pool.Rent("smoke");
        Require(rented != null, "풀 대여가 실패했습니다.");
        pool.Return(rented);
        Require(pool.IdleCount("smoke") >= 1, "풀 반납이 대기열에 들어가지 않았습니다.");
        Destroy(template);

        // 3) 확장 계약: 해금 → 스냅샷 → 복원
        var upgrades = gameObject.AddComponent<UpgradeManager>();
        string id = tables.Upgrades[0].id;
        Require(upgrades.Grant(id), "해금 Grant가 실패했습니다.");
        Require(upgrades.IsUnlocked(id), "해금 상태가 기록되지 않았습니다.");
        var snapshot = upgrades.Capture();
        upgrades.Restore(Array.Empty<string>());
        Require(!upgrades.IsUnlocked(id), "스냅샷 복원 후에도 해금이 남아 있습니다.");
        upgrades.Restore(snapshot);
        Require(upgrades.IsUnlocked(id), "스냅샷 복원이 해금을 되살리지 못했습니다.");

        // 4) 이벤트 허브: 발행이 구독자에게 도달하는지 확인
        bool received = false;
        void Handler(GameEvents.UpgradeInfo _info)
        {
            received = true;
        }
        GameEvents.UpgradeGranted += Handler;
        GameEvents.RaiseUpgradeGranted(tables.Upgrades[0]);
        GameEvents.UpgradeGranted -= Handler;
        Require(received, "GameEvents 발행이 구독자에게 도달하지 않았습니다.");

        Debug.Log("MYJG_SMOKE_PASS: 테이블 로드·검증, 씬 로컬 풀 왕복, 해금·스냅샷 복원, 이벤트 허브 왕복 확인.");
        Destroy(gameObject);
        Application.Quit(0);
    }
}
#endif
