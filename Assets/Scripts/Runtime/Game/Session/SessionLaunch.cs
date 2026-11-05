using UnityEngine;

// Title에서 Game으로, 재시작 때 다음 Game으로 시드를 넘긴다. Game을 직접 실행하면 테이블 기본값을 쓴다.
public static class SessionLaunch
{
    static bool HAS_SEED;
    static int SEED;

    public static void SetSeed(int _seed)
    {
        HAS_SEED = true;
        SEED = _seed;
    }

    public static int ResolveSeed(int _defaultSeed) => HAS_SEED ? SEED : _defaultSeed;

    // Enter Play Mode Options로 도메인 리로드를 꺼도 정적 상태는 매 실행 초기화된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        HAS_SEED = false;
        SEED = 0;
    }
}
