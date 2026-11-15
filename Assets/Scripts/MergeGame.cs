using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

// Turn loop:
//  1. Aim the cannon, release: every ball you own fires in a stream.
//  2. Balls bounce down through the president blocks. Each hit takes the ball's number off the
//     block's hit points. Two balls with the same number touching merge into one worth double.
//  3. Balls that fall out the bottom roll into the pipe, which carries them back up to the cannon.
//  4. When every ball is back: blocks rise one row, a new row comes up, you gain a new "2" ball.
// A block reaching the red line = game over.
public class MergeGame : MonoBehaviour
{
    [Header("Prefabs (built by Bounce Fusion > Build Game Scene)")]
    public GameObject ballPrefab;
    public GameObject[] blockPrefabs;

    [Header("Box")]
    public float boxWidth = 6f;
    public float boxHeight = 10f;
    public float dangerY = 8.8f;
    public float bounciness = 0.6f;
    public float captureY = -0.4f;  // a ball below this has left the box and goes into the pipe

    [Header("Blocks")]
    public int columns = 4;
    public float rowSpacing = 1.6f;
    public float blockSize = 1.365f; // 130% of the original 1.05
    public int startRows = 2;

    [Header("Cannon")]
    public Transform cannon;       // pivot, rotated about Z; barrel points down at 0
    public float muzzleDistance = 1.9f;
    public float shotSpeed = 14f;
    public float fireInterval = 0.1f;
    public float maxAimAngle = 75f;
    public int startBalls = 3;

    [Header("Pipe")]
    public Vector3[] pipePath;     // world points: intake mouth -> ... -> exit above the cannon
    public float pipeSpeed = 14f;

    public Material lineMaterial;
    public Color[] ballColors =
    {
        new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.85f, 0.3f), new Color(1f, 0.55f, 0.25f),
        new Color(0.95f, 0.3f, 0.3f), new Color(0.85f, 0.35f, 0.85f), new Color(0.45f, 0.5f, 1f),
        new Color(0.3f, 0.85f, 0.95f), new Color(0.4f, 0.9f, 0.45f),
    };

    enum State { Intro, Aiming, Playing, Over }

    // intro.mp4 lives in StreamingAssets because WebGL can only play video from a URL.
    // Shown once per launch (restarts skip it). Starts on the first tap: browsers block autoplay with sound.
    static bool introDone;
    VideoPlayer intro;
    bool introStarted;
    AudioSource music;

    readonly List<int> ammo = new List<int>();     // ball values waiting in the cannon
    readonly List<Ball> balls = new List<Ball>();  // balls bouncing in the box
    readonly List<Block> blocks = new List<Block>();
    State state;
    int inPlay;           // fired balls not yet back in the cannon (in box or in pipe)
    bool firing, pressArmed;
    int score, best, turn = 1;
    float turnStart;
    PhysicsMaterial bouncy;
    LineRenderer aimLine;
    Ball preview;
    Camera cam;
    Bounds view;
    float fitAspect;

    float FirstRowY => blockSize * 0.5f + 0.15f;

    // Frames the camera on the scenery (frame, pipe, cannon) so it fills the screen at any aspect ratio.
    void FitCamera()
    {
        fitAspect = cam.aspect;
        cam.orthographicSize = Mathf.Max(view.extents.y, view.extents.x / cam.aspect) * 1.06f; // 6% margin, room for the HUD
        cam.transform.position = view.center - cam.transform.forward * 20f;
    }

    void Start()
    {
        cam = Camera.main;
        var rs = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        view = rs[0].bounds;
        foreach (var r in rs) view.Encapsulate(r.bounds);
        FitCamera();
        best = PlayerPrefs.GetInt("best", 0);
        bouncy = new PhysicsMaterial("bouncy") { bounciness = bounciness, dynamicFriction = 0.2f, staticFriction = 0.2f, bounceCombine = PhysicsMaterialCombine.Maximum };

        // side walls and a lid; the bottom is open so balls drop out into the pipe
        float hw = boxWidth / 2f, wallH = boxHeight + 6f;
        Wall(new Vector3(-hw - 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(hw + 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(0, wallH + 0.5f, 0), new Vector3(boxWidth + 2, 1, 2));

        var danger = Line(new Color(1, 0.2f, 0.2f, 0.6f), 0.05f);
        danger.SetPositions(new[] { new Vector3(-hw, dangerY, -0.6f), new Vector3(hw, dangerY, -0.6f) });
        aimLine = Line(new Color(1, 1, 1, 0.35f), 0.06f);

        for (int r = startRows - 1; r >= 0; r--) SpawnRow(FirstRowY + r * rowSpacing, FirstRowY + r * rowSpacing);
        for (int i = 0; i < startBalls; i++) ammo.Add(2);
        state = State.Aiming;

        music = GetComponent<AudioSource>();
        if (!introDone)
        {
            state = State.Intro;
            if (music) music.Stop();
            intro = cam.gameObject.AddComponent<VideoPlayer>();
            intro.playOnAwake = false;
            intro.url = Application.streamingAssetsPath + "/intro.mp4";
            intro.renderMode = VideoRenderMode.CameraNearPlane;
            intro.targetCamera = cam;
            intro.aspectRatio = VideoAspectRatio.FitInside;
            intro.loopPointReached += _ => EndIntro();
            intro.errorReceived += (_, __) => EndIntro(); // missing/unplayable video: just start the game
        }
    }

    void EndIntro()
    {
        if (state != State.Intro) return;
        introDone = true;
        if (intro) Destroy(intro);
        state = State.Aiming;
        if (music) music.Play();
    }

    void Update()
    {
        var pointer = Pointer.current;
        if (!Mathf.Approximately(cam.aspect, fitAspect)) FitCamera(); // window resized / phone rotated
        if (state == State.Intro)
        {
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                if (!introStarted) { introStarted = true; intro.Play(); }
                else EndIntro(); // tap again to skip
            }
            return;
        }
        if (state == State.Over)
        {
            if (pointer != null && pointer.press.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        balls.RemoveAll(b => b == null);
        foreach (var b in balls.ToArray())
        {
            if (b.transform.position.y < captureY) { StartCoroutine(Return(b)); continue; }
            Unstick(b);
        }

        if (state == State.Aiming)
        {
            Aim();
            ShowPreview();
            if (pointer != null && pointer.press.wasPressedThisFrame) pressArmed = true;
            if (pressArmed && pointer != null && pointer.press.wasReleasedThisFrame) StartCoroutine(FireAll());
        }
        else if (state == State.Playing)
        {
            if (!firing && inPlay == 0) EndTurn();
            else if (!firing && Time.time - turnStart > 20f) RecallAll(); // safety: never wait forever
        }
    }

    void Aim()
    {
        if (Pointer.current == null) return;
        // where the pointer's ray hits the play plane z=0 (correct for a tilted or perspective camera)
        var ray = cam.ScreenPointToRay(Pointer.current.position.ReadValue());
        if (Mathf.Abs(ray.direction.z) < 1e-4f) return;
        Vector3 w = ray.GetPoint(-ray.origin.z / ray.direction.z);
        Vector2 dir = (Vector2)(w - cannon.position);
        if (dir.sqrMagnitude < 0.01f) return;
        float angle = Mathf.Clamp(Vector2.SignedAngle(Vector2.down, dir), -maxAimAngle, maxAimAngle);
        cannon.rotation = Quaternion.Euler(0, 0, angle);

        Vector3 a = Muzzle();
        aimLine.enabled = true;
        aimLine.SetPositions(new[] { a, a + AimDir() * 6f });
    }

    Vector3 AimDir() => cannon.rotation * Vector3.down;
    Vector3 Muzzle() => cannon.position + AimDir() * muzzleDistance;

    // frozen copy of the next ball sitting in the muzzle while you aim
    void ShowPreview()
    {
        if (preview != null && preview.value != ammo[0]) { Destroy(preview.gameObject); preview = null; }
        if (preview == null)
        {
            preview = SpawnBall(ammo[0], Muzzle(), track: false);
            Freeze(preview.gameObject, true);
        }
        preview.transform.position = Muzzle();
    }

    IEnumerator FireAll()
    {
        state = State.Playing;
        firing = true;
        pressArmed = false;
        aimLine.enabled = false;
        turnStart = Time.time;
        if (preview != null) { Destroy(preview.gameObject); preview = null; }

        Vector3 dir = AimDir();
        int count = ammo.Count; // balls returning mid-stream wait for next turn
        for (int i = 0; i < count; i++)
        {
            int value = ammo[0];
            ammo.RemoveAt(0);
            var b = SpawnBall(value, Muzzle(), track: true);
            b.GetComponent<Rigidbody>().linearVelocity = dir * shotSpeed;
            inPlay++;
            yield return new WaitForSeconds(fireInterval);
        }
        firing = false;
    }

    // ball left the box: slide to the pipe mouth, ride the pipe up, drop into the cannon
    IEnumerator Return(Ball b)
    {
        balls.Remove(b);
        b.merged = true; // stop it merging or hitting while in the pipe
        Freeze(b.gameObject, true);
        var t = b.transform;
        if (pipePath != null && pipePath.Length > 0)
        {
            var points = new List<Vector3> { new Vector3(t.position.x, pipePath[0].y, pipePath[0].z) };
            points.AddRange(pipePath);
            foreach (var p in points)
                while (t && (t.position - p).sqrMagnitude > 0.0001f)
                {
                    t.position = Vector3.MoveTowards(t.position, p, pipeSpeed * Time.deltaTime);
                    yield return null;
                }
        }
        if (b) { ammo.Add(b.value); Destroy(b.gameObject); }
        inPlay--;
    }

    void RecallAll()
    {
        foreach (var b in balls.ToArray()) StartCoroutine(Return(b));
        turnStart = Time.time;
    }

    // a ball resting on a flat block top would never leave: give it a shove
    void Unstick(Ball b)
    {
        var rb = b.GetComponent<Rigidbody>();
        b.stillTime = rb.linearVelocity.sqrMagnitude < 0.09f ? b.stillTime + Time.deltaTime : 0f;
        if (b.stillTime > 1f)
        {
            rb.linearVelocity = new Vector3(Random.Range(-3f, 3f), -1f, 0);
            b.stillTime = 0f;
        }
    }

    void EndTurn()
    {
        turn++;
        ammo.Add(2); // a new ball every turn
        Rise();
        if (state != State.Over) state = State.Aiming;
    }

    static void Freeze(GameObject go, bool frozen)
    {
        go.GetComponent<Rigidbody>().isKinematic = frozen;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = !frozen;
    }

    static int Log2(int v) => Mathf.RoundToInt(Mathf.Log(v, 2));

    Ball SpawnBall(int value, Vector3 pos, bool track)
    {
        var go = Instantiate(ballPrefab, pos, Quaternion.identity);
        float size = Mathf.Min(0.5f + 0.06f * (Log2(value) - 1), 1.1f);
        go.transform.localScale = Vector3.one * size;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.sharedMaterial = bouncy;
        go.GetComponent<Rigidbody>().mass = size * size;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", ballColors[(Log2(value) - 1) % ballColors.Length]);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            if (r.GetComponent<TextMesh>() == null) r.SetPropertyBlock(mpb);

        var b = go.GetComponent<Ball>();
        b.value = value;
        b.label.text = value.ToString();
        b.game = this;
        if (track) balls.Add(b);
        return b;
    }

    // Moves every block up one row and brings a new row up from under the box.
    void Rise()
    {
        blocks.RemoveAll(b => b == null);
        foreach (var b in blocks) b.targetY += rowSpacing;
        SpawnRow(FirstRowY - rowSpacing, FirstRowY);
        foreach (var b in blocks)
            if (b.targetY + blockSize * 0.5f > dangerY) { EndGame(); break; }
    }

    void SpawnRow(float y, float targetY)
    {
        float colW = boxWidth / columns;
        var free = new List<int>();
        for (int c = 0; c < columns; c++) free.Add(c);
        int count = Random.Range(1, columns); // never a full row, so balls can drop through
        for (int i = 0; i < count; i++)
        {
            int k = Random.Range(0, free.Count);
            int c = free[k];
            free.RemoveAt(k);
            var pos = new Vector3(-boxWidth / 2f + colW * (c + 0.5f), y, 0);
            var go = Instantiate(blockPrefabs[Random.Range(0, blockPrefabs.Length)], pos, Quaternion.identity);
            go.transform.localScale = Vector3.one * blockSize;
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.sharedMaterial = bouncy;
            var b = go.GetComponent<Block>();
            b.targetY = targetY;
            b.SetHp(turn * Random.Range(1, 4) + 2);
            blocks.Add(b);
        }
    }

    public void HitBlock(Ball ball, Block block)
    {
        if (state == State.Over || ball.merged || block.hp <= 0) return;
        score += Mathf.Min(ball.value, block.hp);
        block.SetHp(block.hp - ball.value);
        if (block.hp <= 0)
        {
            blocks.Remove(block);
            Destroy(block.gameObject);
        }
    }

    public void Merge(Ball a, Ball b)
    {
        if (state == State.Over || !balls.Contains(a) || !balls.Contains(b)) return;
        a.merged = b.merged = true;
        balls.Remove(a);
        balls.Remove(b);
        Vector3 pos = (a.transform.position + b.transform.position) / 2f;
        Vector3 vel = (a.GetComponent<Rigidbody>().linearVelocity + b.GetComponent<Rigidbody>().linearVelocity) / 2f;
        Destroy(a.gameObject);
        Destroy(b.gameObject);

        int value = a.value * 2;
        score += value;
        inPlay--; // two balls became one
        var n = SpawnBall(value, pos, track: true);
        n.GetComponent<Rigidbody>().linearVelocity = vel + Vector3.up * 2f; // little hop
        StartCoroutine(Pop(n.transform, n.transform.localScale.x));
    }

    IEnumerator Pop(Transform t, float size)
    {
        for (float k = 0; k < 1f && t; k += Time.deltaTime / 0.15f)
        {
            t.localScale = Vector3.one * size * Mathf.Lerp(0.6f, 1f, k);
            yield return null;
        }
        if (t) t.localScale = Vector3.one * size;
    }

    void EndGame()
    {
        state = State.Over;
        if (score > best) PlayerPrefs.SetInt("best", best = score);
        aimLine.enabled = false;
    }

    void Wall(Vector3 pos, Vector3 size)
    {
        var w = new GameObject("Wall");
        w.transform.SetParent(transform);
        w.transform.position = pos;
        var c = w.AddComponent<BoxCollider>();
        c.size = size;
        c.sharedMaterial = bouncy;
    }

    LineRenderer Line(Color col, float width)
    {
        var lr = new GameObject("Line").AddComponent<LineRenderer>();
        lr.transform.SetParent(transform);
        lr.material = lineMaterial;
        lr.startColor = lr.endColor = col;
        lr.startWidth = lr.endWidth = width;
        lr.positionCount = 2;
        return lr;
    }

    void OnGUI()
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = Screen.height / 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        s.normal.textColor = Color.white;
        if (state == State.Intro)
        {
            s.alignment = introStarted ? TextAnchor.LowerRight : TextAnchor.MiddleCenter;
            if (introStarted) s.fontSize /= 2;
            GUI.Label(new Rect(0, 0, Screen.width - 10, Screen.height - 10), introStarted ? "tap to skip" : "TAP TO PLAY", s);
            return;
        }
        GUI.Label(new Rect(0, 10, Screen.width, Screen.height / 10f), $"{score}   best {best}   turn {turn}   balls x{ammo.Count + inPlay}", s);
        if (state == State.Over)
        {
            s.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "GAME OVER\ntap to restart", s);
        }
    }
}
