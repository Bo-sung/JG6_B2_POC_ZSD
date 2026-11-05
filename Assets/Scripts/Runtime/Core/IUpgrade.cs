using UnityEngine;

// 확장(해금) 효과 계약.
// 테이블의 UpgradeData.id와 1:1로 연결되며, 배열 순서나 컴포넌트 이름에 의존하지 않는다.
// 효과는 UpgradeManager.Register로 등록하고, 해금·스냅샷 복원 시 Apply/Remove가 호출된다.
//   · Id는 불변이어야 한다(같은 컴포넌트에서 두 효과를 수행하면 안 된다).
//   · Apply는 중복 호출되어도 안전해야 한다(같은 대상에 두 번 적용 금지).
//   · Remove는 적용 여부와 무관하게 항상 호출된다(취소·스냅샷 복원·씬 재시작).
public interface IUpgrade
{
    string Id { get; }

    void Apply(GameObject _owner);
    void Remove(GameObject _owner);
}
