
      using System.IO;
      using UnityEditor;
      using UnityEditor.PackageManager;

      [InitializeOnLoad]
      public static class HubForceResolve
      {
          private const string k_ScriptPath = "Assets\\Editor\\HubForceResolve.cs";
          private const string k_EditorFolderPath = "Assets\\Editor";
          private const string k_StateKey = "Hub.ForceResolve.State";
          private const string k_ManifestPath = "Packages/manifest.json";
          private const string k_ManifestContent = "{\r\n  \"dependencies\": {\r\n    \"com.unity.ai.navigation\": \"2.0.14\",\r\n    \"com.unity.collab-proxy\": \"2.12.4\",\r\n    \"com.unity.ide.rider\": \"3.0.40\",\r\n    \"com.unity.ide.visualstudio\": \"2.0.26\",\r\n    \"com.unity.inputsystem\": \"1.20.0\",\r\n    \"com.unity.multiplayer.center\": \"1.0.1\",\r\n    \"com.unity.pipeline\": \"0.10.0-exp.1\",\r\n    \"com.unity.render-pipelines.universal\": \"17.3.0\",\r\n    \"com.unity.test-framework\": \"1.6.0\",\r\n    \"com.unity.timeline\": \"1.8.12\",\r\n    \"com.unity.ugui\": \"2.0.0\",\r\n    \"com.unity.visualscripting\": \"1.9.12\",\r\n    \"com.unity.modules.accessibility\": \"1.0.0\",\r\n    \"com.unity.modules.adaptiveperformance\": \"1.0.0\",\r\n    \"com.unity.modules.ai\": \"1.0.0\",\r\n    \"com.unity.modules.androidjni\": \"1.0.0\",\r\n    \"com.unity.modules.animation\": \"1.0.0\",\r\n    \"com.unity.modules.assetbundle\": \"1.0.0\",\r\n    \"com.unity.modules.audio\": \"1.0.0\",\r\n    \"com.unity.modules.cloth\": \"1.0.0\",\r\n    \"com.unity.modules.director\": \"1.0.0\",\r\n    \"com.unity.modules.imageconversion\": \"1.0.0\",\r\n    \"com.unity.modules.imgui\": \"1.0.0\",\r\n    \"com.unity.modules.jsonserialize\": \"1.0.0\",\r\n    \"com.unity.modules.particlesystem\": \"1.0.0\",\r\n    \"com.unity.modules.physics\": \"1.0.0\",\r\n    \"com.unity.modules.physics2d\": \"1.0.0\",\r\n    \"com.unity.modules.screencapture\": \"1.0.0\",\r\n    \"com.unity.modules.terrain\": \"1.0.0\",\r\n    \"com.unity.modules.terrainphysics\": \"1.0.0\",\r\n    \"com.unity.modules.tilemap\": \"1.0.0\",\r\n    \"com.unity.modules.ui\": \"1.0.0\",\r\n    \"com.unity.modules.uielements\": \"1.0.0\",\r\n    \"com.unity.modules.umbra\": \"1.0.0\",\r\n    \"com.unity.modules.unityanalytics\": \"1.0.0\",\r\n    \"com.unity.modules.unitywebrequest\": \"1.0.0\",\r\n    \"com.unity.modules.unitywebrequestassetbundle\": \"1.0.0\",\r\n    \"com.unity.modules.unitywebrequestaudio\": \"1.0.0\",\r\n    \"com.unity.modules.unitywebrequesttexture\": \"1.0.0\",\r\n    \"com.unity.modules.unitywebrequestwww\": \"1.0.0\",\r\n    \"com.unity.modules.vectorgraphics\": \"1.0.0\",\r\n    \"com.unity.modules.vehicles\": \"1.0.0\",\r\n    \"com.unity.modules.video\": \"1.0.0\",\r\n    \"com.unity.modules.vr\": \"1.0.0\",\r\n    \"com.unity.modules.wind\": \"1.0.0\",\r\n    \"com.unity.modules.xr\": \"1.0.0\",\r\n    \"com.team.logkit\": \"https://github.com/Bo-sung/logkit.git?path=package#v0.7.0\"\r\n  }\r\n}\r\n";

          static HubForceResolve()
          {
              EditorApplication.delayCall += Tick;
          }

          static void Tick()
          {
              // Wait until the Editor is idle so the resolve/self-delete don't fight the import.
              if (EditorApplication.isCompiling || EditorApplication.isUpdating)
              {
                  EditorApplication.delayCall += Tick;
                  return;
              }

              if (SessionState.GetString(k_StateKey, "") == "")
              {
                  // Write the intended manifest inside the Editor process — this is *after*
                  // the Editor's own template-generation and UPM update-dependencies writes,
                  // so nothing can race with (and clobber) the value we put on disk. See
                  // HUB-7048. Guarded so a write failure (locked file, read-only perms, AV on
                  // Windows) still lets the resolve + self-delete run — the project degrades to
                  // the Editor-generated manifest instead of leaving a stray script behind.
                  try
                  {
                      File.WriteAllText(k_ManifestPath, k_ManifestContent);
                  }
                  catch (System.Exception e)
                  {
                      UnityEngine.Debug.LogWarning("[Unity Hub] Failed to restore captured Packages/manifest.json: " + e.Message);
                  }
                  // First idle pass: resolve. May reload, after which Tick runs again.
                  SessionState.SetString(k_StateKey, "resolved");
                  Client.Resolve();
                  EditorApplication.delayCall += Tick;
                  return;
              }

              // Resolved and idle again → remove the script.
              Cleanup();
          }

          static void Cleanup()
          {
              AssetDatabase.DeleteAsset(k_ScriptPath);
              CleanupEmptyEditorFolder();
          }

          static void CleanupEmptyEditorFolder()
          {
              string absolutePath = Path.GetFullPath(k_EditorFolderPath);

              if (!Directory.Exists(absolutePath)) return;

              string[] files = Directory.GetFiles(absolutePath);
              string[] dirs = Directory.GetDirectories(absolutePath);

              bool isEmpty = true;

              if (dirs.Length > 0)
              {
                  isEmpty = false;
              }

              foreach (string file in files)
              {
                  if (!file.EndsWith(".DS_Store"))
                  {
                      isEmpty = false;
                      break;
                  }
              }

              if (isEmpty)
              {
                  AssetDatabase.DeleteAsset(k_EditorFolderPath);
              }
          }
      }