using System;
using System.Collections.Generic;

// 단계 전환과 승패 규칙. Unity에 의존하지 않으며 GameSession이 소유한다.
// Prep → Alert → Combat → Result → (다음 웨이브 Prep | 승리 Ended). 패배는 어느 단계에서든 Ended로 간다.
public sealed class SessionFlow
{
    readonly float prepSeconds;
    readonly float alertSeconds;
    readonly float combatSeconds;
    readonly float timeoutCoreDamagePerEnemy;

    public SessionPhase Phase { get; private set; }
    public SessionResult Result { get; private set; }
    public int WaveNumber { get; private set; }
    public int WaveCount { get; }
    public float Remaining { get; private set; }

    public bool IsLastWave => WaveNumber >= WaveCount;

    public SessionFlow(float _prepSeconds, float _alertSeconds, float _combatSeconds, float _timeoutCoreDamagePerEnemy, int _waveCount)
    {
        if (_waveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_waveCount));
        }
        prepSeconds = _prepSeconds;
        alertSeconds = _alertSeconds;
        combatSeconds = _combatSeconds;
        timeoutCoreDamagePerEnemy = _timeoutCoreDamagePerEnemy;
        WaveCount = _waveCount;
        WaveNumber = 1;
        Phase = SessionPhase.Prep;
        Result = SessionResult.None;
        Remaining = prepSeconds;
    }

    // 시간이 있는 단계의 타이머를 줄인다. 다 됐으면 true. Result와 Ended에서는 시간이 흐르지 않는다.
    public bool Tick(float _deltaSeconds)
    {
        if (Phase != SessionPhase.Prep && Phase != SessionPhase.Alert && Phase != SessionPhase.Combat)
        {
            return false;
        }
        Remaining = Math.Max(0, Remaining - _deltaSeconds);
        return Remaining <= 0;
    }

    public void ExpireTimer()
    {
        Remaining = 0;
    }

    public void Advance()
    {
        switch (Phase)
        {
            case SessionPhase.Prep:
                Phase = SessionPhase.Alert;
                Remaining = alertSeconds;
                break;
            case SessionPhase.Alert:
                Phase = SessionPhase.Combat;
                Remaining = combatSeconds;
                break;
            case SessionPhase.Combat:
                Phase = SessionPhase.Result;
                Remaining = 0;
                break;
            case SessionPhase.Result:
                if (IsLastWave)
                {
                    Phase = SessionPhase.Ended;
                    Result = SessionResult.Victory;
                }
                else
                {
                    WaveNumber++;
                    Phase = SessionPhase.Prep;
                    Remaining = prepSeconds;
                }
                break;
        }
    }

    public void EndDefeat()
    {
        if (Phase == SessionPhase.Ended)
        {
            return;
        }
        Phase = SessionPhase.Ended;
        Result = SessionResult.Defeat;
        Remaining = 0;
    }

    public void RestartWave()
    {
        if (Phase == SessionPhase.Ended)
        {
            return;
        }
        Phase = SessionPhase.Prep;
        Remaining = prepSeconds;
    }

    // DEC-15: 시간 상한 초과 시 남은 적 1기당 핵심 시설 피해.
    public float TimeoutCoreDamage(int _remainingEnemies) => timeoutCoreDamagePerEnemy * Math.Max(0, _remainingEnemies);

    // DEC-14: 파견 인력까지 포함해 전원 사망했을 때만 전멸이다.
    public static bool IsWiped(IEnumerable<SurvivorState> _survivors)
    {
        foreach (SurvivorState survivor in _survivors)
        {
            if (survivor.IsAlive)
            {
                return false;
            }
        }
        return true;
    }

    public static bool CanChangeWork(SessionPhase _phase) => _phase == SessionPhase.Prep;
    public static bool CanChangeDefense(SessionPhase _phase) => _phase == SessionPhase.Prep || _phase == SessionPhase.Alert || _phase == SessionPhase.Combat;
    public static bool CanCommand(SessionPhase _phase) => CanChangeDefense(_phase);
    public static bool CanBuild(SessionPhase _phase) => _phase == SessionPhase.Prep;
    public static bool CanDispatch(SessionPhase _phase) => _phase == SessionPhase.Prep;
    public static bool ProducesScrap(SessionPhase _phase) => _phase == SessionPhase.Prep;
    public static bool RunsExpeditions(SessionPhase _phase) => CanChangeDefense(_phase);
}
