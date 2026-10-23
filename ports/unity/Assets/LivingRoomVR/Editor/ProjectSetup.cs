// One-time project setup + build menu for Living Room VR.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingRoomVR.EditorTools
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/LivingRoomVR/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () => { if (!File.Exists(ScenePath)) Setup(); };
        }

        [MenuItem("Living Room VR/Set Up Project")]
        public static void Setup()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "jaineil32624";
            PlayerSettings.productName = "Living Room VR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.jaineil.livingroomvr.unity");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            AddAlwaysIncludedShaders("LRVR/Baked", "LRVR/BakedTransparent", "LRVR/Lit", "LRVR/LitTransparent", "LRVR/Unlit");
            AddInputAxis("LookX", 3);  // right stick X (4th axis)
            AddInputAxis("LookY", 4);  // right stick Y (5th axis)

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Living Room VR").AddComponent<LivingRoomGame>();
                RenderSettings.skybox = null;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[LRVR] Project set up. Open Assets/LivingRoomVR/Main.unity and press Play.");
        }

        static void AddAlwaysIncludedShaders(params string[] names)
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (!gs) return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (!sh) { Debug.LogWarning("[LRVR] shader not found: " + n); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
        }

        static void AddInputAxis(string name, int joyAxis)
        {
            var im = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset").FirstOrDefault();
            if (!im) return;
            var so = new SerializedObject(im);
            var axes = so.FindProperty("m_Axes");
            for (int i = 0; i < axes.arraySize; i++) if (axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue == name) return;
            axes.InsertArrayElementAtIndex(axes.arraySize);
            var a = axes.GetArrayElementAtIndex(axes.arraySize - 1);
            a.FindPropertyRelative("m_Name").stringValue = name;
            a.FindPropertyRelative("descriptiveName").stringValue = "";
            a.FindPropertyRelative("negativeButton").stringValue = "";
            a.FindPropertyRelative("positiveButton").stringValue = "";
            a.FindPropertyRelative("altNegativeButton").stringValue = "";
            a.FindPropertyRelative("altPositiveButton").stringValue = "";
            a.FindPropertyRelative("gravity").floatValue = 0;
            a.FindPropertyRelative("dead").floatValue = 0.15f;
            a.FindPropertyRelative("sensitivity").floatValue = 1;
            a.FindPropertyRelative("snap").boolValue = false;
            a.FindPropertyRelative("invert").boolValue = false;
            a.FindPropertyRelative("type").intValue = 2;   // joystick axis
            a.FindPropertyRelative("axis").intValue = joyAxis;
            a.FindPropertyRelative("joyNum").intValue = 0;
            so.ApplyModifiedProperties();
        }

        [MenuItem("Living Room VR/Build Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/LivingRoomVR.exe");

        [MenuItem("Living Room VR/Build Android APK")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/LivingRoomVR-Unity.apk");

        [MenuItem("Living Room VR/Build WebGL")]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, "Builds/WebGL");

        static void Build(BuildTarget target, string path)
        {
            Setup();
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[LRVR] Build {target}: {report.summary.result}, {report.summary.totalSize / 1e6:F1} MB, {report.summary.totalErrors} errors -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
