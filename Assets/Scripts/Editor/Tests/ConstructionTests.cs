using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 에셋의 실제 풋프린트와 수리 규칙을 검증한다. 수치는 테스트 입력이다.
public sealed class ConstructionTests
{
    GameObject instance;

    BuildingBase Create(string _name)
    {
        instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + _name + ".prefab"));
        var building = instance.GetComponent<BuildingBase>();
        building.Initialize(new BuildingStatData { cost = 4, maxHp = 120 });
        return building;
    }

    [TearDown]
    public void Cleanup()
    {
        if (instance)
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void WallUsesItsFullFourMetreCollider()
    {
        var wall = Create("Wall");
        wall.PlacementBox(new Vector3(10, 0, 20), Quaternion.identity, out Vector3 center, out Vector3 half, out _);
        Assert.That(center, Is.EqualTo(new Vector3(10, 0.6f, 20)));
        Assert.That(half, Is.EqualTo(new Vector3(2, 0.6f, 0.5f)));
        Assert.That(half.x * 2, Is.GreaterThan(2), "중심이 이웃 격자에 있어도 겹치는 시설이다.");
    }

    [Test]
    public void RotatingWallRotatesItsFootprint()
    {
        var wall = Create("Wall");
        wall.PlacementBox(Vector3.zero, Quaternion.Euler(0, 90, 0), out _, out Vector3 half, out Quaternion orientation);
        Vector3 corner = orientation * new Vector3(half.x, 0, half.z);
        Assert.That(Mathf.Abs(corner.z), Is.EqualTo(2).Within(0.001));
        Assert.That(Mathf.Abs(corner.x), Is.EqualTo(0.5f).Within(0.001));
    }

    [TestCase(1f, 1)]
    [TestCase(30f, 1)]
    [TestCase(31f, 2)]
    [TestCase(119f, 4)]
    public void RepairRoundsDamageFractionUp(float _damage, int _cost)
    {
        var wall = Create("Wall");
        wall.ApplyDamage(_damage);
        Assert.That(wall.RepairCost, Is.EqualTo(_cost));
        wall.Repair();
        Assert.That(wall.Hp, Is.EqualTo(wall.MaxHp));
        Assert.That(wall.RepairCost, Is.Zero);
    }
}
