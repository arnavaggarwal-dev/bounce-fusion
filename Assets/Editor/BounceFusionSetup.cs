using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu: Bounce Fusion > Build Game Scene
// Builds materials (from the .mtl files), the numbered-ball prefab, one block prefab per
// president, and a ready-to-play scene.
// Re-run any time; it overwrites Assets/Generated and Assets/Scenes/BounceFusion.unity.
public static class BounceFusionSetup
{
    const string Gen = "Assets/Generated";
    const string BallObj = "Assets/ball_marge.obj";

    // President blocks. Euler = rotation applied to the model so its face looks at
    // the camera (camera sits at -Z looking +Z). Calibration knob: tweak if a face points the wrong way.
    static readonly (string obj, Vector3 euler)[] Presidents =
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

    // Builds the scene, then a WebGL player into docs/ (published to GitHub Pages by build.bat).
    [MenuItem("Bounce Fusion/Build WebGL")]
    public static void BuildWebGL()
    {
        Build();
        // GitHub Pages can't serve pre-compressed Brotli/Gzip files with the right headers
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/BounceFusion.unity" }, "docs", BuildTarget.WebGL, BuildOptions.None);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        EditorUtility.DisplayDialog("Bounce Fusion", ok ? "WebGL build done in docs/. Run build.bat to publish." : "WebGL build failed, see Console.", "OK");
    }

    [MenuItem("Bounce Fusion/Build Game Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Gen }))
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid)); // drop prefabs from older versions

        // Blocks: kinematic president shapes with a hit-point label.
        var blockPrefabs = new GameObject[Presidents.Length];
        for (int i = 0; i < Presidents.Length; i++)
        {
            // runtime-scaled MeshColliders need readable meshes in builds
            if (AssetImporter.GetAtPath(Presidents[i].obj) is ModelImporter imp && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var root = new GameObject($"Block_{Path.GetFileNameWithoutExtension(Presidents[i].obj)}");
            Normalize(Model(Presidents[i].obj, root.transform, Presidents[i].euler), Vector3.one, Vector3.zero, uniform: true);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().convex = true;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var block = root.AddComponent<Block>();
            block.label = Label(root.transform, new Vector3(0, -0.28f, -0.8f), 0.06f, Color.white);
            block.label.text = "1";
            blockPrefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, $"{Gen}/{root.name}.prefab");
            Object.DestroyImmediate(root);
        }

        // Ball: unit sphere collider, rotation frozen so the number stays upright.
        var ballMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ballMat.SetFloat("_Smoothness", 0.6f);
        AssetDatabase.CreateAsset(ballMat, $"{Gen}/Ball.mat");
        var ballRoot = new GameObject("Ball");
        var ballModel = Model(BallObj, ballRoot.transform, Vector3.zero);
        Normalize(ballModel, Vector3.one, Vector3.zero, uniform: true);
        foreach (var r in ballModel.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => ballMat);
        ballRoot.AddComponent<SphereCollider>().radius = 0.5f;
        var ballRb = ballRoot.AddComponent<Rigidbody>();
        ballRb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        ballRb.interpolation = RigidbodyInterpolation.Interpolate;
        ballRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var ball = ballRoot.AddComponent<Ball>();
        ball.label = Label(ballRoot.transform, new Vector3(0, 0, -0.55f), 0.07f, new Color(0.1f, 0.1f, 0.12f));
        ball.label.text = "2";
        var ballPrefab = PrefabUtility.SaveAsPrefabAsset(ballRoot, $"{Gen}/Ball.prefab");
        Object.DestroyImmediate(ballRoot);

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
        gm.ballPrefab = ballPrefab;
        gm.blockPrefabs = blockPrefabs;
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

    // Number label facing the camera (TextMesh with the built-in font).
    static TextMesh Label(Transform parent, Vector3 localPos, float charSize, Color color)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.fontStyle = FontStyle.Bold;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        return tm;
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
