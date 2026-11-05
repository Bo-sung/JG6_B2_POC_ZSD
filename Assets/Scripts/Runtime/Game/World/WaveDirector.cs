using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 테이블 편성으로 두 진입로에 적을 스폰한다. 런타임 생성은 적 프리팹 Instantiate만 한다.
[DisallowMultipleComponent]
public sealed class WaveDirector : MonoBehaviour
{
    const string LANE_WEST = "west";

    [Serializable]
    sealed class EnemyPrefab
    {
        public string enemyId;
        public EnemyUnit prefab;
    }

    readonly struct PendingSpawn
    {
        public readonly EnemyData data;
        public readonly EnemyUnit prefab;
        public readonly Transform point;

        public PendingSpawn(EnemyData _data, EnemyUnit _prefab, Transform _point)
        {
            data = _data;
            prefab = _prefab;
            point = _point;
        }
    }

    [SerializeField] EnemyPrefab[] enemyPrefabs = Array.Empty<EnemyPrefab>();
    [SerializeField] Transform westSpawn;
    [SerializeField] Transform eastSpawn;
    [SerializeField] Transform enemyRoot;
    // 스폰 지점 주변 흩뿌림 반경과 NavMesh 탐색 반경. 겹침 방지용 배치 보정값이다.
    [SerializeField] float spawnSpread = 3f;
    [SerializeField] float navSampleRadius = 6f;

    readonly Queue<PendingSpawn> pending = new Queue<PendingSpawn>();
    readonly List<EnemyUnit> alive = new List<EnemyUnit>();
    GameSession session;
    System.Random random;
    float spawnInterval;
    float nextSpawnIn;

    public IReadOnlyList<EnemyUnit> Alive => alive;
    public int RemainingCount => pending.Count + LiveCount();
    public bool IsCleared => RemainingCount == 0;

    public void Initialize(GameSession _session, System.Random _random)
    {
        session = _session;
        random = _random;
    }

    void Update()
    {
        alive.RemoveAll(_enemy => !_enemy || _enemy.IsDead);
        if (pending.Count == 0 || session.Phase != SessionPhase.Combat)
        {
            return;
        }
        nextSpawnIn -= Time.deltaTime;
        while (pending.Count > 0 && nextSpawnIn <= 0)
        {
            Spawn(pending.Dequeue());
            nextSpawnIn += spawnInterval;
        }
    }

    // 묶음을 번갈아 내보내 두 진입로에 고르게 스폰한다.
    public void BeginWave(WaveData _wave)
    {
        pending.Clear();
        spawnInterval = _wave.spawnInterval;
        nextSpawnIn = 0;
        WaveSpawnData[] spawns = _wave.spawns;
        var left = new int[spawns.Length];
        int total = 0;
        for (int index = 0; index < spawns.Length; index++)
        {
            left[index] = spawns[index].count;
            total += spawns[index].count;
        }
        while (total > 0)
        {
            for (int index = 0; index < spawns.Length; index++)
            {
                if (left[index] > 0)
                {
                    left[index]--;
                    total--;
                    pending.Enqueue(CreatePending(spawns[index]));
                }
            }
        }
    }

    // 남은 적을 모두 없앤다. 제거 전 남아 있던 수를 돌려준다.
    public int ClearRemaining()
    {
        int count = RemainingCount;
        pending.Clear();
        foreach (EnemyUnit enemy in alive)
        {
            if (enemy)
            {
                Destroy(enemy.gameObject);
            }
        }
        alive.Clear();
        return count;
    }

    // 세션이 끝나면 남은 적은 그 자리에 멈춘다.
    public void Halt()
    {
        pending.Clear();
        foreach (EnemyUnit enemy in alive)
        {
            if (enemy)
            {
                enemy.Halt();
            }
        }
    }

    int LiveCount()
    {
        int count = 0;
        foreach (EnemyUnit enemy in alive)
        {
            if (enemy && !enemy.IsDead)
            {
                count++;
            }
        }
        return count;
    }

    PendingSpawn CreatePending(WaveSpawnData _spawn)
    {
        EnemyData data = Array.Find(TableManager.Instance.Gameplay.enemies, _enemy => _enemy.id == _spawn.enemyId);
        EnemyPrefab entry = Array.Find(enemyPrefabs, _entry => _entry.enemyId == _spawn.enemyId);
        if (entry == null || !entry.prefab)
        {
            throw new InvalidOperationException($"적 프리팹이 연결되지 않았습니다: {_spawn.enemyId}");
        }
        return new PendingSpawn(data, entry.prefab, _spawn.lane == LANE_WEST ? westSpawn : eastSpawn);
    }

    void Spawn(PendingSpawn _spawn)
    {
        var offset = new Vector3((float)(random.NextDouble() * 2 - 1), 0, (float)(random.NextDouble() * 2 - 1)) * spawnSpread;
        Vector3 position = _spawn.point.position + offset;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
        {
            position = hit.position;
        }
        EnemyUnit enemy = Instantiate(_spawn.prefab, position, _spawn.point.rotation, enemyRoot);
        enemy.Initialize(_spawn.data, session, (float)random.NextDouble());
        alive.Add(enemy);
    }
}
