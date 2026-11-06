using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// G4 XP, 능력치 성장, 특성 3종 검증(TD-40~45, V2-IMPL-07, V2-IMPL-14).
public sealed class ExperienceTests
{
    static GameplayTableData Table => TableManager.Instance.RequireValid().Gameplay;

    static SurvivorState MakeSurvivor(int _combat = 1, int _work = 1, int _exploration = 1, string _trait = "", InjuryData _injury = null)
    {
        var data = new SurvivorData
        {
            id = "test_s",
            displayName = "테스터",
            combat = _combat,
            work = _work,
            exploration = _exploration,
            maxHp = 100,
            trait = _trait,
        };
        return new SurvivorState(data, false, _injury, Table.experience.traitMultiplier);
    }

    [Test]
    public void WorkTimeIsNotStockedWhileWaveCapIsReached()
    {
        var worker = MakeSurvivor(_work: 1);
        var tracker = new ExperienceTracker(Table.experience, _id => worker);
        float perXp = Table.experience.workSecondsPerXp;
        int cap = Table.experience.workWaveCap;

        for (int index = 0; index < cap; index++)
        {
            tracker.TickWorkTime(worker.Id, perXp);
        }
        Assert.That(tracker.GetProgress(worker.Id).WaveWorkXp, Is.EqualTo(cap));
        tracker.TickWorkTime(worker.Id, perXp * 100);
        Assert.That(tracker.GetProgress(worker.Id).WorkSeconds, Is.Zero, "상한 동안의 시간은 쌓이지 않는다.");

        tracker.ResetWave();
        tracker.TickWorkTime(worker.Id, perXp);
        Assert.That(tracker.GetProgress(worker.Id).WaveWorkXp, Is.EqualTo(1), "다음 웨이브에 한꺼번에 지급되지 않는다.");
    }

    [Test]
    public void NeededXpAndRollover()
    {
        var survivor = MakeSurvivor(_combat: 1);
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        Assert.That(tracker.NeededXp(survivor.Combat), Is.EqualTo(20));

        // 25 XP 지급: 필요 20 소비 후 능력치 2로 성장, 초과분 5 이월, 다음 필요 XP는 40(TD-40).
        int gained = tracker.AddDebugExperience(survivor.Id, StatType.Combat, 25);
        Assert.That(gained, Is.EqualTo(25));
        Assert.That(survivor.Combat, Is.EqualTo(2));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.EqualTo(5));
        Assert.That(tracker.NeededXp(survivor.Combat), Is.EqualTo(40));
    }

    [Test]
    public void MaxStatRejectsXp()
    {
        var survivor = MakeSurvivor(_combat: 5);
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        int gained = tracker.AddDebugExperience(survivor.Id, StatType.Combat, 10);
        Assert.That(gained, Is.Zero);
        Assert.That(survivor.Combat, Is.EqualTo(5));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.Zero);
    }

    [Test]
    public void StatsAreIndependent()
    {
        var survivor = MakeSurvivor(_combat: 1, _work: 1, _exploration: 1);
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        tracker.AddDebugExperience(survivor.Id, StatType.Combat, 10);

        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.EqualTo(10));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Work), Is.Zero);
        Assert.That(tracker.GetXp(survivor.Id, StatType.Exploration), Is.Zero);
        Assert.That(survivor.Combat, Is.EqualTo(1));
        Assert.That(survivor.Work, Is.EqualTo(1));
        Assert.That(survivor.Exploration, Is.EqualTo(1));
    }

    [Test]
    public void WaveAndExpeditionCapsAndReset()
    {
        var survivor = MakeSurvivor(_combat: 2, _work: 2);
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        // 전투 웨이브 상한 12(TD-41)
        tracker.RecordCombatHit(survivor.Id); // 3
        for (int i = 0; i < 10; i++)
        {
            tracker.RecordEnemyKilled(new[] { survivor.Id }); // 2씩
        }
        Assert.That(tracker.GetProgress(survivor.Id).WaveCombatXp, Is.EqualTo(12));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.EqualTo(12));

        // 작업 웨이브 상한 10(TD-42)
        for (int i = 0; i < 5; i++)
        {
            tracker.RecordTreatmentComplete(survivor.Id); // 3씩
        }
        Assert.That(tracker.GetProgress(survivor.Id).WaveWorkXp, Is.EqualTo(10));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Work), Is.EqualTo(10));

        // 탐사 건당 상한 8(TD-43)
        int expXp = tracker.CalculateExpeditionXp(true, true, false); // 4 + 2 + 2 = 8
        Assert.That(expXp, Is.EqualTo(8));

        // 웨이브 리셋 시 웨이브 획득량 초기화, 총 누적 XP는 보존
        tracker.ResetWave();
        Assert.That(tracker.GetProgress(survivor.Id).WaveCombatXp, Is.Zero);
        Assert.That(tracker.GetProgress(survivor.Id).WaveWorkXp, Is.Zero);
        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.EqualTo(12));
        Assert.That(tracker.GetXp(survivor.Id, StatType.Work), Is.EqualTo(10));
    }

    [Test]
    public void CombatFirstHitAndKillParticipation()
    {
        var s1 = MakeSurvivor(_combat: 2);
        var s2 = MakeSurvivor(_combat: 2);
        var dict = new Dictionary<string, SurvivorState> { { s1.Id, s1 }, { "s2", s2 } };
        var tracker = new ExperienceTracker(Table.experience, _id => dict.TryGetValue(_id, out var s) ? s : null);

        // 첫 타격 시 3
        int hitXp = tracker.RecordCombatHit(s1.Id);
        Assert.That(hitXp, Is.EqualTo(3));
        Assert.That(tracker.GetXp(s1.Id, StatType.Combat), Is.EqualTo(3));

        // 같은 웨이브 추가 타격은 0
        int secondHitXp = tracker.RecordCombatHit(s1.Id);
        Assert.That(secondHitXp, Is.Zero);
        Assert.That(tracker.GetXp(s1.Id, StatType.Combat), Is.EqualTo(3));

        // 적 처치 시 피해를 준 모든 인력 2씩(TD-41)
        tracker.RecordEnemyKilled(new[] { s1.Id, "s2" });
        Assert.That(tracker.GetXp(s1.Id, StatType.Combat), Is.EqualTo(5));
        Assert.That(tracker.GetXp("s2", StatType.Combat), Is.EqualTo(2));
    }

    [Test]
    public void IdleUnitReceivesZeroCombatXp()
    {
        var idle = MakeSurvivor(_combat: 2);
        var active = MakeSurvivor(_combat: 2);
        var dict = new Dictionary<string, SurvivorState> { { "idle", idle }, { "active", active } };
        var tracker = new ExperienceTracker(Table.experience, _id => dict.TryGetValue(_id, out var s) ? s : null);

        tracker.RecordCombatHit("active");
        tracker.RecordEnemyKilled(new[] { "active" });

        Assert.That(tracker.GetXp("idle", StatType.Combat), Is.Zero);
        Assert.That(tracker.GetXp("active", StatType.Combat), Is.EqualTo(5));
    }

    [Test]
    public void WorkTimeThirtySecondsPerXpAndSubSecondRollover()
    {
        var worker = MakeSurvivor(_work: 2);
        var tracker = new ExperienceTracker(Table.experience, _id => worker);

        // 45초 작업: 1 XP 획득, 15초 이월(TD-42)
        tracker.TickWorkTime(worker.Id, 45f);
        Assert.That(tracker.GetXp(worker.Id, StatType.Work), Is.EqualTo(1));
        Assert.That(tracker.GetProgress(worker.Id).WorkSeconds, Is.EqualTo(15f).Within(0.001f));

        // 15초 추가(합 60초): 1 XP 추가 획득, 0초 잔여
        tracker.TickWorkTime(worker.Id, 15f);
        Assert.That(tracker.GetXp(worker.Id, StatType.Work), Is.EqualTo(2));
        Assert.That(tracker.GetProgress(worker.Id).WorkSeconds, Is.Zero.Within(0.001f));
    }

    [Test]
    public void TreatmentCompletionAwardsThreeXp()
    {
        var worker = MakeSurvivor(_work: 2);
        var tracker = new ExperienceTracker(Table.experience, _id => worker);

        int gained = tracker.RecordTreatmentComplete(worker.Id);
        Assert.That(gained, Is.EqualTo(3));
        Assert.That(tracker.GetXp(worker.Id, StatType.Work), Is.EqualTo(3));
    }

    [Test]
    public void ExpeditionXpRules()
    {
        var tracker = new ExperienceTracker(Table.experience, _id => MakeSurvivor());

        // 기본 귀환: 4
        Assert.That(tracker.CalculateExpeditionXp(false, false, false), Is.EqualTo(4));
        // 사건 직접 대응: +2 (합 6)
        Assert.That(tracker.CalculateExpeditionXp(true, false, false), Is.EqualTo(6));
        // 조우 생존: +2 (합 6)
        Assert.That(tracker.CalculateExpeditionXp(false, true, false), Is.EqualTo(6));
        // 둘 다: 4 + 2 + 2 = 8
        Assert.That(tracker.CalculateExpeditionXp(true, true, false), Is.EqualTo(8));
        // 철수: 절반(내림) (TD-43)
        Assert.That(tracker.CalculateExpeditionXp(false, false, true), Is.EqualTo(2)); // 4 / 2 = 2
        Assert.That(tracker.CalculateExpeditionXp(true, false, true), Is.EqualTo(3));  // 6 / 2 = 3
        Assert.That(tracker.CalculateExpeditionXp(true, true, true), Is.EqualTo(4));   // 8 / 2 = 4
    }

    [Test]
    public void DeadSurvivorReceivesNoXp()
    {
        var survivor = MakeSurvivor(_combat: 2);
        survivor.Kill();
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        int gained = tracker.AddDebugExperience(survivor.Id, StatType.Combat, 10);
        Assert.That(gained, Is.Zero);
        Assert.That(tracker.GetXp(survivor.Id, StatType.Combat), Is.Zero);
    }

    [Test]
    public void StatImprovementUpdatesCombatDpsRangeAndSpecialty()
    {
        // 1,1,1 -> General
        var survivor = MakeSurvivor(_combat: 1, _work: 1, _exploration: 1);
        Assert.That(survivor.Specialty, Is.EqualTo(SurvivorSpecialty.General));

        var combat = Table.survivorCombat;
        float baseRange = GameTableRules.SurvivorRange(combat, survivor.Combat);

        // Combat +1
        survivor.ImproveStat(StatType.Combat, Table.experience.maxStat);
        Assert.That(survivor.Combat, Is.EqualTo(2));
        Assert.That(survivor.Specialty, Is.EqualTo(SurvivorSpecialty.Combat), "최고 능력치가 전투 하나이므로 Combat 특화로 자동 갱신");

        float newRange = GameTableRules.SurvivorRange(combat, survivor.Combat);
        Assert.That(newRange, Is.GreaterThan(baseRange), "사거리는 전투 능력치에 비례(DEC-34)");
    }

    [Test]
    public void TraitMultiplierAndInjuryProduct()
    {
        var marksman = MakeSurvivor(_combat: 3, _trait: "marksman", _injury: Table.injury);
        var foreman = MakeSurvivor(_work: 3, _trait: "foreman", _injury: Table.injury);
        var scout = MakeSurvivor(_exploration: 3, _trait: "scout", _injury: Table.injury);

        // 정상 상태: 특성 ×1.2
        Assert.That(marksman.CombatEfficiency, Is.EqualTo(1.2f).Within(0.0001f));
        Assert.That(foreman.WorkEfficiency, Is.EqualTo(1.2f).Within(0.0001f));
        Assert.That(scout.ExplorationEfficiency, Is.EqualTo(1.2f).Within(0.0001f));

        // 경상 상태: 특성 1.2 × 경상 0.75 = 0.9(TD-44, V2-IMPL-07)
        marksman.ApplyDamage(marksman.MaxHp * 0.5f);
        Assert.That(marksman.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(marksman.CombatEfficiency, Is.EqualTo(0.9f).Within(0.0001f));

        foreman.ApplyDamage(foreman.MaxHp * 0.5f);
        Assert.That(foreman.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(foreman.WorkEfficiency, Is.EqualTo(0.9f).Within(0.0001f));

        scout.ApplyDamage(scout.MaxHp * 0.5f);
        Assert.That(scout.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(scout.ExplorationEfficiency, Is.EqualTo(0.9f).Within(0.0001f));
    }

    [Test]
    public void TraitDoesNotChangeRangeMoveSpeedOrStat()
    {
        var marksman = MakeSurvivor(_combat: 3, _trait: "marksman");
        var normal = MakeSurvivor(_combat: 3, _trait: "");

        Assert.That(marksman.Combat, Is.EqualTo(normal.Combat));
        float rangeMarksman = GameTableRules.SurvivorRange(Table.survivorCombat, marksman.Combat);
        float rangeNormal = GameTableRules.SurvivorRange(Table.survivorCombat, normal.Combat);
        Assert.That(rangeMarksman, Is.EqualTo(rangeNormal));
    }

    [Test]
    public void ScoutRiskReductionDoesNotStack()
    {
        var system = new ExpeditionSystem(Table.expeditions, new System.Random(1234), Table.expeditionEvents, new System.Random(5678));
        string regionId = Table.expeditions[0].id;

        float normalDeath = system.DeathChance(regionId, 5, 2, _hasScout: false);
        float scoutDeath = system.DeathChance(regionId, 5, 2, _hasScout: true);
        Assert.That(scoutDeath, Is.EqualTo(normalDeath * 0.8f).Within(0.0001f));

        // 파티에 scout이 여럿이어도 bool로 전달되므로 중첩하지 않는다(TD-44).
        bool hasScout1 = TraitRules.PartyHasTrait(new[] { "scout" }, TraitRules.SCOUT);
        bool hasScout2 = TraitRules.PartyHasTrait(new[] { "scout", "scout" }, TraitRules.SCOUT);
        Assert.That(hasScout1, Is.True);
        Assert.That(hasScout2, Is.True);

        // 사건 피해 확률 ×0.8
        float normalSearchChance = ExpeditionEventRules.SearchDamageChance(Table.expeditionEvents, false);
        float scoutSearchChance = ExpeditionEventRules.SearchDamageChance(Table.expeditionEvents, true);
        Assert.That(scoutSearchChance, Is.EqualTo(normalSearchChance * 0.8f).Within(0.0001f));

        // 조우 피해 비율 ×0.8
        float normalEncounter = ExpeditionEventRules.EncounterDamageFraction(Table.expeditionEvents, 5, false);
        float scoutEncounter = ExpeditionEventRules.EncounterDamageFraction(Table.expeditionEvents, 5, true);
        Assert.That(scoutEncounter, Is.EqualTo(normalEncounter * 0.8f).Within(0.0001f));
    }

    [Test]
    public void LogPayloadContracts()
    {
        string gainedId = null;
        string gainedStat = null;
        int gainedAmount = 0;

        string improvedId = null;
        string improvedStat = null;
        int improvedOld = 0;
        int improvedNew = 0;

        GameEvents.ExperienceGained += _info =>
        {
            gainedId = _info.id;
            gainedStat = _info.stat;
            gainedAmount = _info.amount;
        };
        GameEvents.StatImproved += _info =>
        {
            improvedId = _info.id;
            improvedStat = _info.stat;
            improvedOld = _info.old;
            improvedNew = _info.@new;
        };

        var survivor = MakeSurvivor(_combat: 1);
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);

        // 20 XP 주면 1에서 2로 레벨업
        tracker.AddDebugExperience(survivor.Id, StatType.Combat, 20);

        Assert.That(gainedId, Is.EqualTo(survivor.Id));
        Assert.That(gainedStat, Is.EqualTo("Combat"));
        Assert.That(gainedAmount, Is.EqualTo(20));

        Assert.That(improvedId, Is.EqualTo(survivor.Id));
        Assert.That(improvedStat, Is.EqualTo("Combat"));
        Assert.That(improvedOld, Is.EqualTo(1));
        Assert.That(improvedNew, Is.EqualTo(2));
    }

    [Test]
    public void NewJoinSurvivorsShareIdenticalRules()
    {
        var candidateData = Table.recruitment.candidates[0]; // c1 태양, marksman
        var recruit = new SurvivorState(candidateData, _newJoin: true, Table.injury, Table.experience.traitMultiplier);
        var tracker = new ExperienceTracker(Table.experience, _id => recruit);

        Assert.That(recruit.IsNewJoin, Is.True);
        Assert.That(recruit.CombatEfficiency, Is.EqualTo(1.2f).Within(0.0001f));

        int needed = tracker.NeededXp(recruit.Combat);
        tracker.AddDebugExperience(recruit.Id, StatType.Combat, needed);
        Assert.That(recruit.Combat, Is.EqualTo(candidateData.combat + 1));
    }

    [Test]
    public void SameSeedExpeditionOutcomeReproducesWithoutScout()
    {
        var first = new ExpeditionSystem(Table.expeditions, new System.Random(9999));
        var second = new ExpeditionSystem(Table.expeditions, new System.Random(9999));

        string regionId = Table.expeditions[0].id;
        float duration = Table.expeditions[0].durationSeconds;
        var members1 = new[] { new ExpeditionMember("m1", 2, false), new ExpeditionMember("m2", 3, false) };
        var members2 = new[] { new ExpeditionMember("m1", 2, false), new ExpeditionMember("m2", 3, false) };

        first.TryStart(regionId, members1, out _);
        second.TryStart(regionId, members2, out _);

        var outcomes1 = first.Tick(duration);
        var outcomes2 = second.Tick(duration);

        Assert.That(outcomes1.Count, Is.EqualTo(1));
        Assert.That(outcomes2.Count, Is.EqualTo(1));
        Assert.That(outcomes1[0].reward, Is.EqualTo(outcomes2[0].reward));
        Assert.That(outcomes1[0].casualties.Length, Is.EqualTo(outcomes2[0].casualties.Length));
        Assert.That(outcomes1[0].returned.Length, Is.EqualTo(outcomes2[0].returned.Length));
    }

    [Test]
    public void ExpeditionPreviewMatchesResolution()
    {
        var system = new ExpeditionSystem(Table.expeditions, new System.Random(1234), Table.expeditionEvents, new System.Random(1));
        var region = Table.expeditions[0];

        // scout 포함 시 미리보기와 실제 판정이 같은 DeathChance(..., _hasScout: true)를 쓴다
        float previewWithScout = system.DeathChance(region.id, 5, 2, _hasScout: true);
        float directFormula = Mathf.Clamp01(region.deathChanceBase + region.deathChancePerExploration * 5 + region.deathChancePerExtraMember * 1) * 0.8f;
        Assert.That(previewWithScout, Is.EqualTo(directFormula).Within(0.0001f));
    }

    [Test]
    public void SelectionPanelFormatAndLabels()
    {
        var survivor = MakeSurvivor(_combat: 3, _work: 5, _exploration: 1, _trait: "marksman");
        var tracker = new ExperienceTracker(Table.experience, _id => survivor);
        tracker.AddDebugExperience(survivor.Id, StatType.Combat, 12);

        string combatText = tracker.FormatStatProgress("전투", survivor.Combat, StatType.Combat, survivor.Id);
        string workText = tracker.FormatStatProgress("작업", survivor.Work, StatType.Work, survivor.Id);

        Assert.That(combatText, Is.EqualTo("전투 3 (XP 12/60)"));
        Assert.That(workText, Is.EqualTo("작업 5 (최대)"));

        Assert.That(SurvivorLabels.TraitDescription("marksman"), Is.EqualTo("사격 전문 (전투 효율 ×1.2)"));
        Assert.That(SurvivorLabels.TraitDescription("foreman"), Is.EqualTo("숙련 작업자 (작업 효율 ×1.2)"));
        Assert.That(SurvivorLabels.TraitDescription("scout"), Is.EqualTo("정찰 전문가 (탐사 효율 ×1.2 · 위험 ×0.8)"));
        Assert.That(SurvivorLabels.TraitDescription(""), Is.EqualTo("없음"));
        Assert.That(SurvivorLabels.TraitDescription(null), Is.EqualTo("없음"));
    }
}
