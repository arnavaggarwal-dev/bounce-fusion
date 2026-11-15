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
    const string BallObj = "Assets/3d/ball_marge.obj";

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
    const float CameraTilt = 8f; // degrees looking down at the box; 0 = straight on

    // Builds the scene, then a WebGL player into docs/ (published to GitHub Pages by build.bat).
    [MenuItem("Bounce Fusion/Build WebGL")]
    public static void BuildWebGL()
    {
        Build();
        // GitHub Pages can't serve pre-compressed Brotli/Gzip files with the right headers
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.template = "PROJECT:Fullscreen"; // Assets/WebGLTemplates/Fullscreen: canvas fills the browser window
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/BounceFusion.unity" }, "docs", BuildTarget.WebGL, BuildOptions.None);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        if (Application.isBatchMode) { if (!ok) throw new System.Exception("WebGL build failed"); } // non-zero exit code
        else EditorUtility.DisplayDialog("Bounce Fusion", ok ? "WebGL build done in docs/. Run build.bat to publish." : "WebGL build failed, see Console.", "OK");
    }

    [MenuItem("Bounce Fusion/Build Game Scene")]
    public static void Build()
    {
        AssetDatabase.DeleteAsset(Gen); // everything in here is rebuilt below; drops leftovers from replaced models
        AssetDatabase.CreateFolder("Assets", "Generated");

        // Blocks: kinematic president shapes with a hit-point label.
        var blockPrefabs = new GameObject[Presidents.Length];
        for (int i = 0; i < Presidents.Length; i++)
        {
            // runtime-scaled MeshColliders need readable meshes in builds
            if (AssetImporter.GetAtPath(Presidents[i].obj) is ModelImporter imp && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var root = new GameObject($"Block_{Path.GetFileNameWithoutExtension(Presidents[i].obj)}");
            var blockModel = Model(Presidents[i].obj, root.transform, Presidents[i].euler);
            blockPrefabs[i] = SaveTarget(root, blockModel, Block.Kind.President, new Vector3(0, -0.28f, -0.8f), Color.white);
        }

        // Star and chest: stand-in shapes made in code. To use your own model instead, replace the
        // GameObject passed as `model` with Model("Assets/3d/yourmodel.obj", root.transform, Vector3.zero).
        var gold = Mat("Gold", new Color(1f, 0.78f, 0.15f), 0.75f);
        var starRoot = new GameObject("Star");
        var starModel = new GameObject("StarModel");
        starModel.transform.SetParent(starRoot.transform, false);
        starModel.AddComponent<MeshFilter>().sharedMesh = StarMesh();
        starModel.AddComponent<MeshRenderer>().sharedMaterial = gold;
        var starPrefab = SaveTarget(starRoot, starModel, Block.Kind.Star, new Vector3(0, -0.04f, -0.6f), new Color(0.3f, 0.15f, 0f));

        var chestRoot = new GameObject("Chest");
        var chestModel = new GameObject("ChestModel");
        chestModel.transform.SetParent(chestRoot.transform, false);
        var wood = Mat("Wood", new Color(0.55f, 0.3f, 0.12f), 0.2f);
        var woodDark = Mat("WoodDark", new Color(0.4f, 0.2f, 0.07f), 0.2f);
        Cube(chestModel.transform, new Vector3(0, -0.15f, 0), new Vector3(1f, 0.6f, 0.7f), wood);        // body
        Cube(chestModel.transform, new Vector3(0, 0.28f, 0), new Vector3(1.04f, 0.28f, 0.74f), woodDark); // lid
        Cube(chestModel.transform, new Vector3(0, 0.12f, 0), new Vector3(1.06f, 0.07f, 0.76f), gold);     // band
        Cube(chestModel.transform, new Vector3(0, 0.08f, -0.38f), new Vector3(0.2f, 0.24f, 0.08f), gold); // lock
        var chestPrefab = SaveTarget(chestRoot, chestModel, Block.Kind.Chest, new Vector3(0, -0.2f, -0.8f), Color.white);

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
        cam.orthographicSize = 8.8f;
        // slight downward tilt; MergeGame re-frames position/size at runtime to fill the screen
        var camRot = Quaternion.Euler(CameraTilt, 0, 0);
        cam.transform.SetPositionAndRotation(new Vector3(0, 6.9f, 0) - camRot * Vector3.forward * 20f, camRot);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.1f, 0.18f);

        // frame.obj is modelled facing +Z (open front, patterned back plate at z=0). The camera looks from -Z,
        // so turn it around: open side to the camera, plate behind. Its depth (-0.7..1.3) encloses the play plane z=0.
        var frame = new GameObject("Frame");
        Normalize(Model("Assets/3d/frame.obj", frame.transform, new Vector3(0, 180, 0)), new Vector3(BoxW + 1f, BoxH + 1f, 2f), new Vector3(0, BoxH / 2f - 0.5f, 0.3f), uniform: false);

        var cannon = new GameObject("Cannon");
        cannon.transform.position = new Vector3(0, BoxH + 2.2f, 0);
        var barrel = new GameObject("Barrel");
        barrel.transform.SetParent(cannon.transform, false);
        // barrel 2 units long, breech slightly above the pivot, muzzle pointing down
        Normalize(Model("Assets/3d/cannon.obj", barrel.transform, CannonEuler), new Vector3(2f, 2f, 2f), new Vector3(0, -0.6f, 0), uniform: true);

        // Pipe: intake mouth under the box, tube up the right side, exit dropping into the cannon.
        // Landmarks of pipe.obj in 0..1 of its bounds (tube side on the right), measured from its vertices.
        // Re-measure these if pipe.obj is swapped for a differently shaped model.
        const float mouthU = 0.47f, bottomV = 0.045f, tubeU = 0.907f, topV = 0.956f, exitU = 0.093f, exitV = 0.845f;
        // Scaled uniformly (keeps the model's proportions) so the exit sits just above the cannon and the
        // mouth just under the box; the tube lands wherever the model's width puts it.
        var pipe = new GameObject("Pipe");
        var pipeModel = Model("Assets/3d/pipe.obj", pipe.transform, Vector3.zero);
        if (ExitOnRight(pipeModel)) pipeModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
        const float exitY = BoxH + 3f, mouthY = -0.9f;
        var pipeSize = GetBounds(pipeModel).size;
        float pipeH = (exitY - mouthY) / (exitV - bottomV), pipeW = pipeH * pipeSize.x / pipeSize.y;
        float pipeX0 = -exitU * pipeW, pipeY0 = mouthY - bottomV * pipeH;
        Normalize(pipeModel, new Vector3(pipeW, pipeH, 1f), new Vector3(pipeX0 + pipeW / 2f, pipeY0 + pipeH / 2f, 0.2f), uniform: true);
        Vector3 P(float u, float v) => new Vector3(pipeX0 + u * pipeW, pipeY0 + v * pipeH, -0.7f);
        var pipePath = new[] { P(mouthU, bottomV), P(tubeU, bottomV), P(tubeU, topV), P(exitU, topV), P(exitU, exitV) };

        var lineMat = new Material(Shader.Find("Sprites/Default"));
        AssetDatabase.CreateAsset(lineMat, $"{Gen}/Line.mat");

        var gm = new GameObject("Game").AddComponent<MergeGame>();
        gm.ballPrefab = ballPrefab;
        gm.blockPrefabs = blockPrefabs;
        gm.starPrefab = starPrefab;
        gm.chestPrefab = chestPrefab;
        gm.cannon = cannon.transform;
        gm.muzzleDistance = 1.9f;
        gm.boxWidth = BoxW;
        gm.boxHeight = BoxH;
        gm.lineMaterial = lineMat;
        gm.pipePath = pipePath;

        var music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/audio/theme.mp3");
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

    // Turns root (with `model` as a child) into a target prefab: unit-sized model, convex colliders,
    // kinematic body, Block component and a hit-point label.
    static GameObject SaveTarget(GameObject root, GameObject model, Block.Kind kind, Vector3 labelPos, Color labelColor)
    {
        Normalize(model, Vector3.one, Vector3.zero, uniform: true);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().convex = true;
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var block = root.AddComponent<Block>();
        block.kind = kind;
        block.model = model.transform; // turned by Block; the label is a sibling so it stays upright
        block.label = Label(root.transform, labelPos, 0.06f, labelColor);
        block.label.text = "1";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Gen}/{root.name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material Mat(string name, Color color, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, $"{Gen}/{name}.mat");
        return m;
    }

    static void Cube(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.transform.SetParent(parent, false);
        c.transform.localPosition = pos;
        c.transform.localScale = size;
        c.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // Flat-shaded 5-point star, 1 unit across, 0.3 deep, facing -Z (the camera).
    static Mesh StarMesh()
    {
        const int n = 10; const float outer = 0.5f, inner = 0.22f, d = 0.15f;
        Vector3 P(int i, float z)
        {
            float r = i % 2 == 0 ? outer : inner, a = Mathf.PI / 2f + i * Mathf.PI * 2f / n;
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
        }
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < n; i++) // front and back fans (own vertices so faces stay flat-shaded)
        {
            int k = v.Count;
            v.AddRange(new[] { new Vector3(0, 0, -d), P(i + 1, -d), P(i, -d), new Vector3(0, 0, d), P(i, d), P(i + 1, d) });
            t.AddRange(new[] { k, k + 1, k + 2, k + 3, k + 4, k + 5 });
        }
        for (int i = 0; i < n; i++) // sides
        {
            int k = v.Count;
            v.AddRange(new[] { P(i, -d), P(i + 1, -d), P(i + 1, d), P(i, d) });
            t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
        }
        var mesh = new Mesh { name = "Star" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Gen}/StarMesh.asset");
        return mesh;
    }

    // The pipe's exit stub is the only geometry at ~83-89% of its height; tells which side it's on.
    static bool ExitOnRight(GameObject model)
    {
        var b = GetBounds(model);
        float sum = 0; int n = 0;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = mf.transform.TransformPoint(v);
                float t = (w.y - b.min.y) / b.size.y;
                if (t > 0.8f && t < 0.89f) { sum += w.x; n++; }
            }
        return n > 0 && sum / n > b.center.x;
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
                    cur.SetColor("_BaseColor", new Color(F(parts[1]), F(parts[2]), F(parts[3]), cur.GetColor("_BaseColor").a));
                    break;
                case "d" when cur != null: // opacity: 1 = solid, 0 = invisible
                    var c = cur.GetColor("_BaseColor");
                    c.a = F(parts[1]);
                    cur.SetColor("_BaseColor", c);
                    SetTransparent(cur, c.a < 1f);
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

    // URP Lit surface type switch (same properties the material inspector sets).
    static void SetTransparent(Material m, bool on)
    {
        m.SetFloat("_Surface", on ? 1 : 0);
        m.SetFloat("_Blend", 0); // alpha blend
        m.SetFloat("_SrcBlend", (float)(on ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
        m.SetFloat("_DstBlend", (float)(on ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)(on ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
        m.SetFloat("_ZWrite", on ? 0 : 1);
        m.SetOverrideTag("RenderType", on ? "Transparent" : "Opaque");
        if (on) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = on ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
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
