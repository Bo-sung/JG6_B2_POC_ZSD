using System.Collections.Generic;
using UnityEngine;

public sealed class LogGameLifecycle : MonoBehaviour
{
    LogSession session;
    double elapsed;

    void OnEnable()
    {
        GameEvents.SessionStarted += OnSessionStarted;
        GameEvents.AssignmentChanged += OnAssignmentChanged;
        GameEvents.ExpeditionStarted += OnExpeditionStarted;
        GameEvents.ExpeditionResolved += OnExpeditionResolved;
        GameEvents.BuildingPlaced += OnBuildingPlaced;
        GameEvents.WaveStarted += OnWaveStarted;
        GameEvents.SurvivorDied += OnSurvivorDied;
        GameEvents.WaveEnded += OnWaveEnded;
        GameEvents.SessionEnded += OnSessionEnded;
        GameEvents.VisitorArrived += OnVisitorArrived;
        GameEvents.RecruitmentAccepted += OnRecruitmentAccepted;
        GameEvents.RecruitmentRejected += OnRecruitmentRejected;
        GameEvents.SurvivorJoined += OnSurvivorJoined;
        LogKitSetup.BeforeShutdown += EndForQuit;
    }

    void OnDisable()
    {
        GameEvents.SessionStarted -= OnSessionStarted;
        GameEvents.AssignmentChanged -= OnAssignmentChanged;
        GameEvents.ExpeditionStarted -= OnExpeditionStarted;
        GameEvents.ExpeditionResolved -= OnExpeditionResolved;
        GameEvents.BuildingPlaced -= OnBuildingPlaced;
        GameEvents.WaveStarted -= OnWaveStarted;
        GameEvents.SurvivorDied -= OnSurvivorDied;
        GameEvents.WaveEnded -= OnWaveEnded;
        GameEvents.SessionEnded -= OnSessionEnded;
        GameEvents.VisitorArrived -= OnVisitorArrived;
        GameEvents.RecruitmentAccepted -= OnRecruitmentAccepted;
        GameEvents.RecruitmentRejected -= OnRecruitmentRejected;
        GameEvents.SurvivorJoined -= OnSurvivorJoined;
        LogKitSetup.BeforeShutdown -= EndForQuit;
        EndForQuit();
    }

    void FixedUpdate()
    {
        if (session != null && !session.IsClosed)
        {
            elapsed += Time.fixedDeltaTime;
        }
    }

    void OnSessionStarted(GameEvents.SessionStartInfo _info)
    {
        Close();
        elapsed = 0;
        session = new LogSession(FileLogStore.CreateDefault());
        Write("SessionStart", new Dictionary<string, object>
        {
            { "seed", _info.seed },
            { "physicsAiMayDifferWithSameSeed", true }
        });
    }

    void OnAssignmentChanged(GameEvents.AssignmentInfo _info)
    {
        Write("AssignmentChanged", new Dictionary<string, object>
        {
            { "unit", _info.survivorId }, { "work", _info.work }, { "defense", _info.defense }
        });
    }

    void OnExpeditionStarted(GameEvents.ExpeditionStartInfo _info)
    {
        Write("ExpeditionStarted", new Dictionary<string, object>
        {
            { "region", _info.regionId }, { "party", _info.party }
        });
    }

    void OnExpeditionResolved(GameEvents.ExpeditionResultInfo _info)
    {
        Write("ExpeditionResolved", new Dictionary<string, object>
        {
            { "region", _info.regionId }, { "reward", _info.reward },
            { "casualties", _info.casualties }, { "returned", _info.returned }
        });
    }

    void OnBuildingPlaced(GameEvents.BuildingInfo _info)
    {
        Write("BuildingPlaced", new Dictionary<string, object>
        {
            { "type", _info.type.ToString() },
            { "position", new Dictionary<string, object>
                {
                    { "x", _info.position.x }, { "y", _info.position.y }, { "z", _info.position.z }
                }
            },
            { "cost", _info.cost }, { "id", _info.id }
        });
    }

    void OnWaveStarted(GameEvents.WaveStartInfo _info)
    {
        Write("WaveStarted", new Dictionary<string, object> { { "index", _info.waveIndex } });
    }

    void OnSurvivorDied(GameEvents.SurvivorDeathInfo _info)
    {
        Write("SurvivorDied", new Dictionary<string, object> { { "id", _info.survivorId }, { "cause", _info.cause } });
    }

    void OnWaveEnded(GameEvents.WaveEndInfo _info)
    {
        Write("WaveEnded", new Dictionary<string, object>
        {
            { "index", _info.waveIndex }, { "coreHP", _info.coreHp }, { "alive", _info.alive }, { "scrap", _info.scrap }
        });
    }

    void OnVisitorArrived(GameEvents.VisitorArrivedInfo _info)
    {
        Write("VisitorArrived", new Dictionary<string, object>
        {
            { "candidateId", _info.candidateId }, { "wave", _info.wave }
        });
    }

    void OnRecruitmentAccepted(GameEvents.RecruitmentAcceptedInfo _info)
    {
        Write("RecruitmentAccepted", new Dictionary<string, object>
        {
            { "candidateId", _info.candidateId }, { "cost", _info.cost }, { "aliveCount", _info.aliveCount }
        });
    }

    void OnRecruitmentRejected(GameEvents.RecruitmentRejectedInfo _info)
    {
        Write("RecruitmentRejected", new Dictionary<string, object>
        {
            { "reason", _info.reason }
        });
    }

    void OnSurvivorJoined(GameEvents.SurvivorJoinedInfo _info)
    {
        Write("SurvivorJoined", new Dictionary<string, object>
        {
            { "newId", _info.newId }, { "source", _info.source }
        });
    }

    void OnSessionEnded(GameEvents.SessionEndInfo _info)
    {
        Write("SessionEnded", new Dictionary<string, object> { { "result", _info.result.ToString() }, { "reason", _info.reason } });
        Close();
    }

    void Write(string _type, Dictionary<string, object> _payload)
    {
        session?.Write(_type, elapsed, _payload);
    }

    void Close()
    {
        session?.Close();
    }

    void EndForQuit()
    {
        OnSessionEnded(new GameEvents.SessionEndInfo(SessionResult.None, "quit"));
    }
}

