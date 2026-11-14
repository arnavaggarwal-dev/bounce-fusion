using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Aim the cannon, release to fire a numbered ball. Balls bounce around the box and smash
// president blocks: each hit takes the ball's number off the block's hit points. Two balls with
// the same number merge into double. Every few shots the blocks rise one row and a new row
// comes up from the bottom. A block reaching the red line = game over.
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

    [Header("Blocks")]
    public int columns = 4;
    public float rowSpacing = 1.4f;
    public float blockSize = 1.05f;
    public int startRows = 2;
    public int shotsPerRise = 3;

    [Header("Cannon")]
    public Transform cannon;       // pivot, rotated about Z; barrel points down at 0
    public float muzzleDistance = 1.9f;
    public float shotSpeed = 14f;
    public float maxAimAngle = 75f;
    public float reloadTime = 0.45f;

    public Material lineMaterial;
    public Color[] ballColors =
    {
        new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.85f, 0.3f), new Color(1f, 0.55f, 0.25f),
        new Color(0.95f, 0.3f, 0.3f), new Color(0.85f, 0.35f, 0.85f), new Color(0.45f, 0.5f, 1f),
        new Color(0.3f, 0.85f, 0.95f), new Color(0.4f, 0.9f, 0.45f),
    };

    readonly List<Ball> balls = new List<Ball>();
    readonly List<Block> blocks = new List<Block>();
    PhysicsMaterial bouncy;
    LineRenderer aimLine;
    Ball loaded;
    int nextValue, score, best, shots;
    float reloadAt, overTimer;
    bool gameOver;
    Camera cam;

    float FirstRowY => blockSize * 0.5f + 0.15f;

    void Start()
    {
        cam = Camera.main;
        best = PlayerPrefs.GetInt("best", 0);
        bouncy = new PhysicsMaterial("bouncy") { bounciness = bounciness, dynamicFriction = 0.2f, staticFriction = 0.2f, bounceCombine = PhysicsMaterialCombine.Maximum };

        float hw = boxWidth / 2f, wallH = boxHeight + 6f;
        Wall(new Vector3(-hw - 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(hw + 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(0, -0.5f, 0), new Vector3(boxWidth + 2, 1, 2));

        var danger = Line(new Color(1, 0.2f, 0.2f, 0.6f), 0.05f);
        danger.SetPositions(new[] { new Vector3(-hw, dangerY, -0.6f), new Vector3(hw, dangerY, -0.6f) });
        aimLine = Line(new Color(1, 1, 1, 0.35f), 0.06f);

        for (int r = startRows - 1; r >= 0; r--) SpawnRow(FirstRowY + r * rowSpacing, FirstRowY + r * rowSpacing);
        nextValue = RandomValue();
        Reload();
    }

    void Update()
    {
        if (gameOver)
        {
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        Aim();
        if (loaded == null && Time.time >= reloadAt) Reload();
        if (loaded != null) loaded.transform.position = Muzzle();
        if (loaded != null && Pointer.current != null && Pointer.current.press.wasReleasedThisFrame) Fire();

        // also lose if settled balls pile up over the line for 2s
        balls.RemoveAll(b => b == null);
        bool above = false;
        foreach (var b in balls)
            if (b != loaded && Time.time - b.spawnTime > 1.5f && b.transform.position.y + b.transform.localScale.y * 0.5f > dangerY)
                above = true;
        overTimer = above ? overTimer + Time.deltaTime : 0f;
        if (overTimer > 2f) EndGame();
    }

    void Aim()
    {
        if (Pointer.current == null) return;
        Vector2 sp = Pointer.current.position.ReadValue();
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Abs(cam.transform.position.z)));
        Vector2 dir = (Vector2)(w - cannon.position);
        if (dir.sqrMagnitude < 0.01f) return;
        float angle = Mathf.Clamp(Vector2.SignedAngle(Vector2.down, dir), -maxAimAngle, maxAimAngle);
        cannon.rotation = Quaternion.Euler(0, 0, angle);

        Vector3 a = Muzzle();
        aimLine.SetPositions(new[] { a, a + AimDir() * 6f });
    }

    Vector3 AimDir() => cannon.rotation * Vector3.down;
    Vector3 Muzzle() => cannon.position + AimDir() * muzzleDistance;

    void Reload()
    {
        loaded = SpawnBall(nextValue, Muzzle());
        Freeze(loaded.gameObject, true);
        nextValue = RandomValue();
    }

    void Fire()
    {
        Freeze(loaded.gameObject, false);
        loaded.spawnTime = Time.time;
        loaded.GetComponent<Rigidbody>().linearVelocity = AimDir() * shotSpeed;
        loaded = null;
        reloadAt = Time.time + reloadTime;
        if (++shots % shotsPerRise == 0) Rise();
    }

    static void Freeze(GameObject go, bool frozen)
    {
        go.GetComponent<Rigidbody>().isKinematic = frozen;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = !frozen;
    }

    static int RandomValue() => new[] { 2, 2, 2, 4, 4, 8 }[Random.Range(0, 6)];
    static int Log2(int v) => Mathf.RoundToInt(Mathf.Log(v, 2));

    Ball SpawnBall(int value, Vector3 pos)
    {
        var go = Instantiate(ballPrefab, pos, Quaternion.identity);
        float size = Mathf.Min(0.55f + 0.07f * (Log2(value) - 1), 1.2f);
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
        b.spawnTime = Time.time;
        balls.Add(b);
        return b;
    }

    // Moves every block up one row and brings a new row up from under the floor.
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
        int baseHp = 3 + shots;
        var free = new List<int>();
        for (int c = 0; c < columns; c++) free.Add(c);
        int count = Random.Range(1, columns); // never a full row, so balls can get through
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
            b.SetHp(Mathf.Max(1, Mathf.RoundToInt(baseHp * Random.Range(0.6f, 1.6f))));
            blocks.Add(b);
        }
    }

    public void HitBlock(Ball ball, Block block)
    {
        if (gameOver || block.hp <= 0 || ball == loaded) return;
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
        if (gameOver) return;
        a.merged = b.merged = true;
        Vector3 pos = (a.transform.position + b.transform.position) / 2f;
        Vector3 vel = (a.GetComponent<Rigidbody>().linearVelocity + b.GetComponent<Rigidbody>().linearVelocity) / 2f;
        Destroy(a.gameObject);
        Destroy(b.gameObject);

        int value = a.value * 2;
        score += value;
        var n = SpawnBall(value, pos);
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
        gameOver = true;
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
        var s = new GUIStyle(GUI.skin.label) { fontSize = Screen.height / 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(0, 10, Screen.width, Screen.height / 10f), $"{score}   (best {best})   next {nextValue}", s);
        if (gameOver)
        {
            s.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "GAME OVER\ntap to restart", s);
        }
    }
}
