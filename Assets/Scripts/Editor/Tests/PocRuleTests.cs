using System;
using System.Linq;
using NUnit.Framework;

// 작은 고정 수치는 경계와 확률 검증용 입력이며 게임 밸런스를 정하지 않는다.
public sealed class PocRuleTests
{
    [Test]
    public void SeedAndStreamNameReproduceSequence()
    {
        var first = new SessionRng(12345);
        var second = new SessionRng(12345);
        Random random = first.For("탐사");
        Assert.That(first.For("탐사"), Is.SameAs(random));
        for (int index = 0; index < 100; index++)
        {
            Assert.That(random.Next(), Is.EqualTo(second.For("탐사").Next()));
        }
        Assert.Throws<ArgumentNullException>(() => first.For(null));
    }

    [Test]
    public void ConsumingAnotherStreamDoesNotChangeExpedition()
    {
        var first = new SessionRng(77);
        var second = new SessionRng(77);
        for (int index = 0; index < 100; index++)
        {
            first.For("enemy").Next();
        }
        for (int index = 0; index < 100; index++)
        {
            Assert.That(first.For("expedition").Next(), Is.EqualTo(second.For("expedition").Next()));
        }
        Assert.That(new SessionRng(77).For("enemy").Next(), Is.Not.EqualTo(new SessionRng(77).For("expedition").Next()));
        Assert.That(new SessionRng(78).For("enemy").Next(), Is.Not.EqualTo(new SessionRng(77).For("enemy").Next()));
    }

    [Test]
    public void ScrapNeverBecomesNegative()
    {
        var economy = new ResourceEconomy(2, 0.25f);
        Assert.That(economy.CanAfford(2), Is.True);
        Assert.That(economy.CanAfford(-1), Is.False);
        Assert.That(economy.TrySpend(-1), Is.False);
        Assert.That(economy.TrySpend(3), Is.False);
        Assert.That(economy.Scrap, Is.EqualTo(2));
        Assert.That(economy.TrySpend(2), Is.True);
        Assert.That(economy.TrySpend(1), Is.False);
        Assert.That(economy.Scrap, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => economy.Add(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceEconomy(-1, 1));
        economy.Add(3);
        Assert.That(economy.Scrap, Is.EqualTo(3));
    }

    [Test]
    public void FractionalProductionIsCarriedAcrossTicksAndIdleTime()
    {
        var economy = new ResourceEconomy(0, 0.25f);
        Assert.That(economy.TickProduction(1, 1), Is.Zero);
        Assert.That(economy.TickProduction(1, 1), Is.Zero);
        Assert.That(economy.TickProduction(100, 0), Is.Zero);
        Assert.That(economy.TickProduction(1, 3), Is.EqualTo(1));
        Assert.That(economy.TickProduction(3, 1), Is.EqualTo(1));
        Assert.That(economy.Scrap, Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => economy.TickProduction(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => economy.TickProduction(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => economy.TickProduction(float.NaN, 1));
    }

    [Test]
    public void IntegerOverflowDoesNotCorruptScrap()
    {
        var economy = new ResourceEconomy(int.MaxValue, 1);
        Assert.Throws<OverflowException>(() => economy.Add(1));
        Assert.Throws<OverflowException>(() => economy.TickProduction(1, 1));
        Assert.That(economy.Scrap, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void PartyBoundsAndDuplicateMembersAreRejected()
    {
        ExpeditionRegionData region = Region();
        var system = new ExpeditionSystem(new[] { region }, new Random(1));
        Assert.That(system.TryStart(region.id, Array.Empty<ExpeditionMember>(), out _), Is.False);
        Assert.That(system.TryStart(region.id, Party(4), out _), Is.False);
        Assert.That(system.TryStart(region.id, null, out _), Is.False);
        Assert.That(system.TryStart("missing", Party(1), out _), Is.False);
        var duplicate = new[] { new ExpeditionMember("same", 2), new ExpeditionMember("same", 3) };
        Assert.That(system.TryStart(region.id, duplicate, out _), Is.False);
        Assert.That(system.Active, Is.Empty);
        Assert.That(system.TryStart(region.id, Party(1), out string error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(system.TryStart(region.id, Party(2), out error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(system.Active.Count, Is.EqualTo(1));
    }

    [Test]
    public void DuplicateAcrossRegionsIsRejectedAndInputIsCopied()
    {
        ExpeditionRegionData near = Region();
        ExpeditionRegionData far = Region();
        far.id = "far";
        var system = new ExpeditionSystem(new[] { near, far }, new Random(1));
        ExpeditionMember[] members = Party(1);
        Assert.That(system.TryStart(near.id, members, out _), Is.True);
        members[0] = new ExpeditionMember("changed", 5);
        Assert.That(system.Active[0].party[0], Is.EqualTo("s0"));
        Assert.That(system.TryStart(far.id, Party(1), out _), Is.False);
        Assert.That(system.TryStart(far.id, members, out _), Is.True);
    }

    [Test]
    public void TimeOnlyAdvancesWhenTickedAndCompletionIsEmittedOnce()
    {
        ExpeditionRegionData region = Region();
        var system = new ExpeditionSystem(new[] { region }, new Random(1));
        system.TryStart(region.id, Party(3), out _);
        Assert.That(system.Tick(0), Is.Empty);
        Assert.That(system.Active[0].remainingSeconds, Is.EqualTo(region.durationSeconds));
        Assert.That(system.Tick(region.durationSeconds / 2), Is.Empty);
        Assert.That(system.Active[0].remainingSeconds, Is.EqualTo(region.durationSeconds / 2));
        ExpeditionOutcome outcome = system.Tick(region.durationSeconds)[0];
        Assert.That(outcome.regionId, Is.EqualTo(region.id));
        Assert.That(outcome.returned.Length + outcome.casualties.Length, Is.EqualTo(3));
        Assert.That(system.Active, Is.Empty);
        Assert.That(system.Tick(region.durationSeconds), Is.Empty);
        Assert.That(system.TryStart(region.id, Party(1), out _), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Tick(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Tick(-1));
    }

    [Test]
    public void RewardAndDeathCanBothOccurAndAtMostOneMemberDies()
    {
        ExpeditionRegionData region = Region();
        region.rewardChanceBase = 1;
        region.deathChanceBase = 1;
        for (int seed = 0; seed < 100; seed++)
        {
            ExpeditionOutcome outcome = Resolve(region, Party(3), seed);
            Assert.That(outcome.reward, Is.EqualTo(5));
            Assert.That(outcome.casualties.Length, Is.EqualTo(1));
            Assert.That(outcome.returned.Length, Is.EqualTo(2));
            Assert.That(outcome.returned.Intersect(outcome.casualties), Is.Empty);
        }
    }

    [Test]
    public void NoSurvivorMeansNoRewardEvenWhenRewardRollSucceeds()
    {
        ExpeditionRegionData region = Region();
        region.rewardChanceBase = 1;
        region.deathChanceBase = 1;
        ExpeditionOutcome outcome = Resolve(region, Party(1), 42);
        Assert.That(outcome.reward, Is.Zero);
        Assert.That(outcome.returned, Is.Empty);
        Assert.That(outcome.casualties, Is.EqualTo(new[] { "s0" }));
    }

    [Test]
    public void FormulaUsesExplorationSumPartySizeClampingAndHalfUpRounding()
    {
        ExpeditionRegionData region = Region();
        region.rewardChanceBase = 0.1f;
        region.rewardChancePerExploration = 0.2f;
        region.rewardBase = 2;
        region.rewardPerExploration = 0.5f;
        region.deathChanceBase = 0.8f;
        region.deathChancePerExploration = -0.1f;
        region.deathChancePerExtraMember = -0.05f;
        var system = new ExpeditionSystem(new[] { region }, new Random(1));
        Assert.That(system.RewardChance(region.id, 3), Is.EqualTo(0.7f).Within(0.00001f));
        Assert.That(system.DeathChance(region.id, 3, 3), Is.EqualTo(0.4f).Within(0.00001f));
        Assert.That(system.ExpectedReward(region.id, 1), Is.EqualTo(3));
        Assert.That(system.ExpectedReward(region.id, 5), Is.EqualTo(5));
        Assert.That(system.RewardChance(region.id, 100), Is.EqualTo(1));
        Assert.That(system.DeathChance(region.id, 100, 3), Is.Zero);
        region.rewardChanceBase = -2;
        region.deathChanceBase = 2;
        Assert.That(system.RewardChance(region.id, 0), Is.Zero);
        Assert.That(system.DeathChance(region.id, 0, 1), Is.EqualTo(1));
    }

    [Test]
    public void ManySeedsMatchTableFormulaAndIndependentJointFrequency()
    {
        const int SAMPLE_COUNT = 10000;
        const double TOLERANCE = 0.025;
        ExpeditionRegionData region = TableManager.Instance.Gameplay.expeditions.Last();
        ExpeditionMember[] party = Party(region.maxParty);
        int exploration = ExpeditionSystem.TotalExploration(party);
        var formula = new ExpeditionSystem(new[] { region }, new Random(1));
        double rewardChance = formula.RewardChance(region.id, exploration);
        double deathChance = formula.DeathChance(region.id, exploration, party.Length);
        int rewards = 0;
        int deaths = 0;
        int both = 0;
        var victims = new int[party.Length];
        for (int seed = 0; seed < SAMPLE_COUNT; seed++)
        {
            ExpeditionOutcome outcome = Resolve(region, party, seed);
            bool rewarded = outcome.reward > 0;
            bool died = outcome.casualties.Length > 0;
            rewards += rewarded ? 1 : 0;
            deaths += died ? 1 : 0;
            both += rewarded && died ? 1 : 0;
            Assert.That(outcome.casualties.Length, Is.LessThanOrEqualTo(1));
            if (died)
            {
                victims[Array.FindIndex(party, _member => _member.id == outcome.casualties[0])]++;
            }
        }
        Assert.That((double)rewards / SAMPLE_COUNT, Is.EqualTo(rewardChance).Within(TOLERANCE));
        Assert.That((double)deaths / SAMPLE_COUNT, Is.EqualTo(deathChance).Within(TOLERANCE));
        Assert.That((double)both / SAMPLE_COUNT, Is.EqualTo(rewardChance * deathChance).Within(TOLERANCE));
        foreach (int count in victims)
        {
            Assert.That((double)count / SAMPLE_COUNT, Is.EqualTo(deathChance / party.Length).Within(TOLERANCE));
        }
    }

    [Test]
    public void ExpeditionResultsReproduceForSameSeed()
    {
        ExpeditionRegionData region = Region();
        for (int seed = 0; seed < 100; seed++)
        {
            ExpeditionOutcome first = Resolve(region, Party(3), seed);
            ExpeditionOutcome second = Resolve(region, Party(3), seed);
            Assert.That(first.reward, Is.EqualTo(second.reward));
            Assert.That(first.casualties, Is.EqualTo(second.casualties));
            Assert.That(first.returned, Is.EqualTo(second.returned));
        }
    }

    [Test]
    public void EarlierDeathRollDoesNotChangeLaterExpeditionOutcome()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            ExpeditionOutcome afterSafe = SecondOutcome(0, seed);
            ExpeditionOutcome afterDeath = SecondOutcome(1, seed);
            Assert.That(afterDeath.reward, Is.EqualTo(afterSafe.reward));
            Assert.That(afterDeath.casualties, Is.EqualTo(afterSafe.casualties));
            Assert.That(afterDeath.returned, Is.EqualTo(afterSafe.returned));
        }
    }

    [Test]
    public void ActualProjectTablesAreValid()
    {
        TableManager.Reload();
        Assert.That(TableManager.Initialize().IsValid, Is.True);
        Assert.DoesNotThrow(() => TableManager.Instance.RequireValid());
    }

    static ExpeditionRegionData Region()
    {
        return new ExpeditionRegionData
        {
            id = "test",
            durationSeconds = 4,
            minParty = 1,
            maxParty = 3,
            rewardChanceBase = 0.5f,
            rewardBase = 5,
            deathChanceBase = 0.5f
        };
    }

    static ExpeditionMember[] Party(int _count)
    {
        return Enumerable.Range(0, _count).Select(_index => new ExpeditionMember("s" + _index, 1)).ToArray();
    }

    static ExpeditionOutcome SecondOutcome(float _firstDeathChance, int _seed)
    {
        ExpeditionRegionData first = Region();
        first.id = "first";
        first.deathChanceBase = _firstDeathChance;
        ExpeditionRegionData second = Region();
        var system = new ExpeditionSystem(new[] { first, second }, new Random(_seed));
        Assert.That(system.TryStart(first.id, Party(3), out _), Is.True);
        system.Tick(first.durationSeconds);
        Assert.That(system.TryStart(second.id, Party(3), out _), Is.True);
        return system.Tick(second.durationSeconds)[0];
    }

    static ExpeditionOutcome Resolve(ExpeditionRegionData _region, ExpeditionMember[] _party, int _seed)
    {
        var system = new ExpeditionSystem(new[] { _region }, new Random(_seed));
        Assert.That(system.TryStart(_region.id, _party, out _), Is.True);
        return system.Tick(_region.durationSeconds)[0];
    }
}
