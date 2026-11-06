using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayFeedbackTests
{
    [TestCase(5, 0, 11.5f)]
    [TestCase(1, 0, 8.7f)]
    [TestCase(5, 5, 16.5f)]
    public void RangeUsesCombatAndOptionalTowerBonus(int _combat, float _bonus, float _expected)
    {
        var data = new SurvivorCombatData { attackRange = 8, attackRangePerCombat = 0.7f };
        Assert.That(GameTableRules.SurvivorRange(data, _combat, _bonus), Is.EqualTo(_expected).Within(0.0001));
    }

    [Test]
    public void NegativeRangePerCombatIsRejected()
    {
        var tables = TableManager.Instance.RequireValid();
        float saved = tables.Gameplay.survivorCombat.attackRangePerCombat;
        try
        {
            tables.Gameplay.survivorCombat.attackRangePerCombat = -0.1f;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.survivorCombat.attackRangePerCombat"));
            var rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);
        }
        finally
        {
            tables.Gameplay.survivorCombat.attackRangePerCombat = saved;
        }
    }

    [Test]
    public void RosterSpecialtiesMatchUniqueHighestStat()
    {
        var roster = TableManager.Instance.RequireValid().Gameplay.survivors.Select(_data => new SurvivorState(_data)).ToArray();
        Assert.That(roster.Select(_state => _state.Specialty), Is.EqualTo(new[]
        {
            SurvivorSpecialty.Combat, SurvivorSpecialty.Combat, SurvivorSpecialty.Work, SurvivorSpecialty.Work,
            SurvivorSpecialty.Exploration, SurvivorSpecialty.General, SurvivorSpecialty.General,
        }));
    }

    [TestCase(5, 5, 1)]
    [TestCase(1, 5, 5)]
    [TestCase(5, 1, 5)]
    public void AnyTieForHighestIsGeneral(int _combat, int _work, int _exploration)
    {
        var state = new SurvivorState(new SurvivorData { combat = _combat, work = _work, exploration = _exploration, maxHp = 10 });
        Assert.That(state.Specialty, Is.EqualTo(SurvivorSpecialty.General));
    }

    [Test]
    public void SpeedOptionsRequireOneAndPositiveFiniteDistinctValues()
    {
        var valid = new GameTableRules();
        valid.ValidateSpeedOptions(new[] { 1f, 2f, 4f });
        Assert.That(valid.IsValid, Is.True);
        foreach (float[] options in new[] { new[] { 2f }, new[] { 1f, 0f }, new[] { 1f, -2f }, new[] { 1f, float.NaN }, new[] { 1f, float.PositiveInfinity }, new[] { 1f, 1f } })
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("테이블 오류 \\[Gameplay.session.speedOptions\\]"));
            var rules = new GameTableRules();
            rules.ValidateSpeedOptions(options);
            Assert.That(rules.IsValid, Is.False);
        }
    }
}
