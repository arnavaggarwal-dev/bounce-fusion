using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu: Bounce Fusion > Build Game Scene
// Builds materials (from the .mtl files), one prefab per tier, and a ready-to-play scene.
// Re-run any time; it overwrites Assets/Generated and Assets/Scenes/BounceFusion.unity.
public static class BounceFusionSetup
{
    const string Gen = "Assets/Generated";

    // Tier order lowest -> highest. Euler = rotation applied to the model so its face looks at
    // the camera (camera sits at -Z looking +Z). Calibration knob: tweak if a face points the wrong way.
    static readonly (string obj, Vector3 euler)[] Tiers =
    {
        ("Assets/3d/Trump_cube.obj",          new Vector3(0, 0, 0)),
        ("Assets/3d/Obama_pyramid.obj",       new Vector3(0, 0, 0)),
        ("Assets/3d/Biden_rhombohedron.obj",  new Vector3(0, 0, 0)),
        ("Assets/3d/Netanyahu_star.obj",      new Vector3(-90, 0, 0)), // modelled lying flat, face up
        ("Assets/3d/KimJongUn_sphere.obj",    new Vector3(0, 180, 0)),
        ("Assets/3d/Putin_littleman.obj",     new Vector3(0, 180, 0)),
    };
    static readonly Vector3 CannonEuler = Vector3.zero; // barrel modelled along Y; use (0,0,180) if it fires backwards
    const float BoxW = 6f, BoxH = 10f;

    // CI entry point (GitHub Actions / GameCI): build the scene, then a WebGL player.
    public static void BuildWebGL()
    {
        Build();
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-customBuildPath");
        string output = i >= 0 && i + 1 < args.Length ? args[i + 1] : "build/WebGL/WebGL";
        // GitHub Pages can't serve pre-compressed Brotli/Gzip files with the right headers
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/BounceFusion.unity" }, output, BuildTarget.WebGL, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    [MenuItem("Bounce Fusion/Build Game Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh();

        var prefabs = new GameObject[Tiers.Length];
        for (int i = 0; i < Tiers.Length; i++)
        {
            // runtime-scaled MeshColliders need readable meshes in builds
            if (AssetImporter.GetAtPath(Tiers[i].obj) is ModelImporter imp && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var root = new GameObject($"Tier{i}_{Path.GetFileNameWithoutExtension(Tiers[i].obj)}");
            Normalize(Model(Tiers[i].obj, root.transform, Tiers[i].euler), Vector3.one, Vector3.zero, uniform: true);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().convex = true;
            var rb = root.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<Piece>().tier = i;
            prefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, $"{Gen}/{root.name}.prefab");
            Object.DestroyImmediate(root);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = 7.5f;
        cam.transform.SetPositionAndRotation(new Vector3(0, 6.3f, -20), Quaternion.identity);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.1f, 0.18f);

        var frame = new GameObject("Frame");
        Normalize(Model("Assets/3d/frame.obj", frame.transform, Vector3.zero), new Vector3(BoxW + 1f, BoxH + 1f, 1.2f), new Vector3(0, BoxH / 2f - 0.5f, 0.3f), uniform: false);

        var cannon = new GameObject("Cannon");
        cannon.transform.position = new Vector3(0, BoxH + 2.2f, 0);
        var barrel = new GameObject("Barrel");
        barrel.transform.SetParent(cannon.transform, false);
        // barrel 2 units long, breech slightly above the pivot, muzzle pointing down
        Normalize(Model("Assets/3d/cannon.obj", barrel.transform, CannonEuler), new Vector3(2f, 2f, 2f), new Vector3(0, -0.6f, 0), uniform: true);

        var lineMat = new Material(Shader.Find("Sprites/Default"));
        AssetDatabase.CreateAsset(lineMat, $"{Gen}/Line.mat");

        var gm = new GameObject("Game").AddComponent<MergeGame>();
        gm.tierPrefabs = prefabs;
        gm.cannon = cannon.transform;
        gm.muzzleDistance = 1.9f;
        gm.boxWidth = BoxW;
        gm.boxHeight = BoxH;
        gm.lineMaterial = lineMat;

        var music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/audio/Hygiene.mp3");
        if (music)
        {
            var src = gm.gameObject.AddComponent<AudioSource>();
            src.clip = music;
            src.loop = true;
            src.playOnAwake = true;
            src.volume = 0.5f;
        }

        const string scenePath = "Assets/Scenes/BounceFusion.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath);
        list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Bounce Fusion scene built. Press Play.");
    }

    // Instantiates an imported model under parent with rotation and materials built from its .mtl.
    static GameObject Model(string objPath, Transform parent, Vector3 euler)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
        if (asset == null) throw new FileNotFoundException("Model not imported: " + objPath);
        var go = Object.Instantiate(asset, parent);
        go.name = asset.name;
        go.transform.localRotation = Quaternion.Euler(euler);

        var mats = MtlMaterials(Path.ChangeExtension(objPath, ".mtl"));
        if (mats.Count == 0) return go;
        var ordered = new List<Material>(mats.Values);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
                shared[i] = shared[i] != null && mats.TryGetValue(shared[i].name, out var m) ? m : ordered[Mathf.Min(i, ordered.Count - 1)];
            r.sharedMaterials = shared;
        }
        return go;
    }

    // Parses newmtl / Kd / map_Kd and makes URP Lit materials. Unity's own OBJ material import is unreliable.
    static Dictionary<string, Material> MtlMaterials(string mtlPath)
    {
        var result = new Dictionary<string, Material>();
        if (!File.Exists(mtlPath)) return result;
        Material cur = null;
        string dir = Path.GetDirectoryName(mtlPath).Replace('\\', '/');
        foreach (var raw in File.ReadAllLines(mtlPath))
        {
            var parts = raw.Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            switch (parts[0])
            {
                case "newmtl":
                    var name = string.Join(" ", parts, 1, parts.Length - 1);
                    var path = $"{Gen}/{name}.mat";
                    cur = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (cur == null) { cur = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(cur, path); }
                    cur.SetFloat("_Smoothness", 0.2f);
                    result[name] = cur;
                    break;
                case "Kd" when cur != null && parts.Length >= 4:
                    cur.SetColor("_BaseColor", new Color(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "map_Kd" when cur != null:
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{parts[parts.Length - 1]}");
                    if (tex) cur.SetTexture("_BaseMap", tex);
                    break;
            }
        }
        foreach (var m in result.Values) EditorUtility.SetDirty(m);
        return result;
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // Scales the model so its bounds fit `size` (uniform keeps proportions, fitting X/Y) and centers it on `center`.
    static void Normalize(GameObject model, Vector3 size, Vector3 center, bool uniform)
    {
        var t = model.transform;
        var b = GetBounds(model);
        Vector3 k = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / Mathf.Max(b.size.z, 1e-4f));
        if (uniform) k = Vector3.one * Mathf.Min(k.x, k.y);
        // scale is applied in the model's local (pre-rotation) axes, so map world-axis factors back
        t.localScale = uniform ? k : Abs(Quaternion.Inverse(t.localRotation) * k);
        b = GetBounds(model);
        t.position += t.parent.TransformPoint(center) - b.center;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Bounds GetBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
