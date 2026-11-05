using System;

public sealed class ResourceEconomy
{
    readonly double productionPerWork;
    double pendingProduction;
    public int Scrap { get; private set; }

    public ResourceEconomy(int _startScrap, float _scrapPerSecondPerWork)
    {
        if (_startScrap < 0 || _scrapPerSecondPerWork < 0 || float.IsNaN(_scrapPerSecondPerWork) || float.IsInfinity(_scrapPerSecondPerWork))
        {
            throw new ArgumentOutOfRangeException(nameof(_startScrap));
        }
        Scrap = _startScrap;
        productionPerWork = _scrapPerSecondPerWork;
    }

    public bool CanAfford(int _cost)
    {
        return _cost >= 0 && Scrap >= _cost;
    }

    public bool TrySpend(int _cost)
    {
        if (!CanAfford(_cost))
        {
            return false;
        }
        Scrap -= _cost;
        return true;
    }

    public void Add(int _amount)
    {
        if (_amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_amount));
        }
        Scrap = checked(Scrap + _amount);
    }

    public int TickProduction(float _deltaSeconds, int _totalWork)
    {
        if (_deltaSeconds < 0 || float.IsNaN(_deltaSeconds) || float.IsInfinity(_deltaSeconds) || _totalWork < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_deltaSeconds));
        }
        double accumulated = pendingProduction + _deltaSeconds * productionPerWork * _totalWork;
        int paid = checked((int)Math.Floor(accumulated));
        Add(paid);
        // 정수 지급 후 남은 소수는 다음 근무 구간에도 보존한다.
        pendingProduction = accumulated - paid;
        return paid;
    }
}
