using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬 로컬 오브젝트 풀.
// 규칙:
//   1) 풀은 씬 소유다(SceneServices.For<ScenePool>). 전역 정적 풀, 테이블 안의 GameObject는 금지.
//   2) Rent로 빌린 것은 Return으로 반납한다. 반납 오브젝트는 Destroy 대신 비활성화해 재사용한다.
//   3) 씬이 언로드되면 풀과 남은 오브젝트도 함께 사라진다(씬 간 공유 없음).
//   4) 원본 등록은 씬 인스펙터의 entries 또는 코드 Register 중 한 방식으로 통일한다.
// 사용:
//   var pool = ScenePool.Current;
//   var go = pool.Rent("debris", position, rotation);
//   pool.Return(go);
[DisallowMultipleComponent]
public sealed class ScenePool : MonoBehaviour
{
    [Serializable]
    sealed class Entry
    {
        public string key;
        public GameObject prefab;
        [Min(0)] public int prewarm;
    }

    [Tooltip("씬 인스펙터에서 구성하는 원본 목록. key로 찾고 prewarm만큼 미리 생성한다.")]
    [SerializeField] Entry[] entries = Array.Empty<Entry>();

    sealed class Bucket
    {
        public readonly string key;
        public GameObject prefab;
        public Transform container;         // Pooled/<key> 자식. 반납 오브젝트가 머물 곳
        public readonly Queue<GameObject> idle = new();
        public int rented;
        public Bucket(string _key)
        {
            this.key = _key;
        }
    }

    readonly Dictionary<string, Bucket> buckets = new(StringComparer.Ordinal);
    readonly struct LentRef
    {
        public readonly GameObject instance;
        public readonly Bucket bucket;
        public LentRef(GameObject _instance, Bucket _bucket)
        {
            this.instance = _instance;
            this.bucket = _bucket;
        }
    }

    readonly Dictionary<int, LentRef> lent = new();    // 대여 중인 오브젝트(instanceID → 참조)
    Transform poolRoot;
    bool prepared;

    /// <summary>이 씬의 풀. 없으면 만든다.</summary>
    public static ScenePool Current => SceneServices.For<ScenePool>();
    public static ScenePool Of(Scene _scene) => SceneServices.For<ScenePool>(_scene);

    void Awake() => Prepare();
    void OnEnable() => Prepare();

    // 씬에 저장된 설정을 늦어도 첫 사용 전에 반영한다.
    void Prepare()
    {
        if (prepared)
        {
            return;
        }

        prepared = true;
        if (!poolRoot)
        {
            var root = new GameObject("Pooled");
            root.transform.SetParent(transform, false);
            poolRoot = root.transform;
        }
        foreach (var entry in entries ?? Array.Empty<Entry>())
        {
            if (entry != null && !string.IsNullOrEmpty(entry.key) && entry.prefab)
            {
                Register(entry.key, entry.prefab, entry.prewarm);
            }
        }
    }

    // 원본 등록. 같은 key 재등록은 원본만 교체하고 대기열은 유지한다.
    public void Register(string _key, GameObject _prefab, int _prewarm = 0)
    {
        Prepare();
        if (string.IsNullOrEmpty(_key) || !_prefab)
        {
            Debug.LogError($"풀 등록 실패: key='{_key}' 원본={(_prefab ? _prefab.name : "null")}입니다.", this);
            return;
        }
        var bucket = BucketFor(_key);
        bucket.prefab = _prefab;
        for (int i = 0; i < _prewarm; i++)
        {
            bucket.idle.Enqueue(Create(bucket));
        }
    }

    public bool IsRegistered(string _key) => !string.IsNullOrEmpty(_key) && buckets.ContainsKey(_key);

    // 대여: 대기열에서 하나 켜서 주고, 없으면 새로 만든다. 미등록 key는 오류 후 null.
    public GameObject Rent(string _key, Vector3 _position, Quaternion _rotation)
    {
        Prepare();
        if (!buckets.TryGetValue(_key ?? string.Empty, out var bucket) || !bucket.prefab)
        {
            Debug.LogError($"풀에 등록되지 않은 key입니다: '{_key}'. entries 또는 Register로 원본을 먼저 등록하세요.", this);
            return null;
        }
        GameObject instance = null;
        while (bucket.idle.Count > 0 && instance == null)
        {
            instance = bucket.idle.Dequeue();  // 밖에서 파괴된 항목 건너뛰기
        }

        if (instance)
        {
            instance.transform.SetPositionAndRotation(_position, _rotation);
            instance.SetActive(true);
        }
        else
        {
            instance = Create(bucket);
        }

        lent[instance.GetInstanceID()] = new LentRef(instance, bucket);
        bucket.rented++;
        return instance;
    }

    public GameObject Rent(string _key) => Rent(_key, Vector3.zero, Quaternion.identity);

    // 반납: 비활성화 후 container로 옮겨 대기열에 넣는다. 이 풀에서 빌리지 않은 오브젝트는 경고한다.
    public void Return(GameObject _instance)
    {
        if (!_instance)
        {
            return;
        }

        int id = _instance.GetInstanceID();
        if (!lent.TryGetValue(id, out var lentRef))
        {
            Debug.LogWarning($"이 풀에서 대여하지 않은 오브젝트입니다: {_instance.name}", this);
            return;
        }
        var bucket = lentRef.bucket;
        lent.Remove(id);
        bucket.rented--;
        _instance.SetActive(false);
        _instance.transform.SetParent(Container(bucket), false);
        bucket.idle.Enqueue(_instance);
    }

    // 판 초기화: 대여 중인 것을 모두 반납한다(대기열까지 Destroy하지 않고 끈다).
    public void ReturnAll()
    {
        foreach (var pair in new List<KeyValuePair<int, LentRef>>(lent))
        {
            if (pair.Value.instance)
            {
                Return(pair.Value.instance);
            }
            else
            {
                pair.Value.bucket.rented--;
                lent.Remove(pair.Key);
            }
        }
    }

    public int IdleCount(string _key) => !string.IsNullOrEmpty(_key) && buckets.TryGetValue(_key, out var bucket) ? bucket.idle.Count : 0;
    public int RentedCount(string _key) => !string.IsNullOrEmpty(_key) && buckets.TryGetValue(_key, out var bucket) ? bucket.rented : 0;

    Bucket BucketFor(string _key)
    {
        if (!buckets.TryGetValue(_key, out var bucket))
        {
            buckets[_key] = bucket = new Bucket(_key);
        }

        return bucket;
    }

    GameObject Create(Bucket _bucket)
    {
        var instance = Instantiate(_bucket.prefab);
        instance.name = _bucket.prefab.name;
        SceneServices.MoveTo(instance, gameObject.scene);
        instance.transform.SetParent(Container(_bucket), false);
        instance.SetActive(false);
        return instance;
    }

    // key별 컨테이너는 늦게 만든다(실제로 사용된 key만 씬 트리에 보인다).
    Transform Container(Bucket _bucket)
    {
        if (!_bucket.container)
        {
            var container = new GameObject(_bucket.key);
            container.transform.SetParent(poolRoot, false);
            _bucket.container = container.transform;
        }
        return _bucket.container;
    }

    // 씬과 함께 사라지므로 정적 상태는 없다. 도메인 리로드 여부와 무관하게 안전하다.
}
