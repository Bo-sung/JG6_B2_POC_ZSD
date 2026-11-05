// 귀환 예측은 전투가 조기 종료될 수 있음을 명시한다. 밸런스 수치를 복제하지 않는다.
public static class ExpeditionLabels
{
    public static string ReturnTiming(float _duration, SessionPhase _phase, float _remaining)
    {
        if (_phase != SessionPhase.Prep)
        {
            return "준비 단계에서 파견 가능";
        }
        if (_duration <= _remaining)
        {
            return "이번 준비 중 복귀 예상";
        }
        return "이번 경보·전투 중 복귀 예상*";
    }
}
