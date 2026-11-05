using UnityEngine;
using UnityEngine.SceneManagement;

// 씬과 함께 생성·제거되는 씬 로컬 서비스의 접근 창.
// 규칙: GameObject·풀·진행 상태 같은 씬 실행 상태는 전역 싱글톤이나 전역 테이블에 두지 않고,
//       SceneServices가 씬 안에 만든 호스트 오브젝트가 소유한다. 다른 씬의 풀을 공유하지 않는다.
// 사용:
//   var pool = SceneServices.For<ScenePool>();     // 이 씬의 풀(없으면 만든다)
//   SceneServices.For<ScenePool>(otherScene);      // Additive로 열린 씬의 풀
public static class SceneServices
{
    const string RootName = "Scene Services";

    // 씬에 이미 있는 서비스를 찾을 때만 사용(생성하지 않는다).
    public static T Find<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid()) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component) return component;
        }
        return null;
    }

    public static T For<T>(Scene scene) where T : Component => Find<T>(scene) ?? Create<T>(scene);
    public static T For<T>() where T : Component => For<T>(SceneManager.GetActiveScene());

    // 서비스를 씬 안의 호스트 오브젝트에 붙인다. DontDestroyOnLoad 금지(씬과 함께 사라져야 한다).
    static T Create<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid()) return null;
        var host = new GameObject(typeof(T).Name);
        SceneManager.MoveGameObjectToScene(host, scene);
        host.transform.SetParent(Root(scene).transform, false);
        return host.AddComponent<T>();
    }

    static GameObject Root(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == RootName) return root;
        var created = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    // 씬 오브젝트를 특정 씬 소속으로 옮긴다(풀이 만든 오브젝트의 소속 보장).
    public static void MoveTo(GameObject target, Scene scene)
    {
        if (target && scene.IsValid() && target.scene != scene) SceneManager.MoveGameObjectToScene(target, scene);
    }
}
