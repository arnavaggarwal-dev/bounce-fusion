using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Aim the cannon, release to fire. Pieces bounce around the box; two of the same tier merge
// into the next one (2 -> 4 -> 8 ...). Keep a piece above the red line too long = game over.
public class MergeGame : MonoBehaviour
{
    [Header("Tiers (lowest first). Prefabs built by Bounce Fusion > Build Game Scene")]
    public GameObject[] tierPrefabs;
    public float[] tierSizes = { 0.7f, 0.9f, 1.1f, 1.35f, 1.6f, 1.9f };
    public int spawnableTiers = 3;

    [Header("Box")]
    public float boxWidth = 6f;
    public float boxHeight = 10f;
    public float dangerY = 8.8f;
    public float bounciness = 0.55f;

    [Header("Cannon")]
    public Transform cannon;       // pivot, rotated about Z; barrel points down at 0
    public float muzzleDistance = 1.2f;
    public float shotSpeed = 13f;
    public float maxAimAngle = 75f;
    public float reloadTime = 0.45f;

    public Material lineMaterial;

    readonly List<Piece> pieces = new List<Piece>();
    PhysicsMaterial bouncy;
    LineRenderer aimLine;
    Piece loaded;
    GameObject preview;
    int nextTier, score, best;
    float reloadAt, overTimer;
    bool gameOver;
    Camera cam;

    void Start()
    {
        cam = Camera.main;
        best = PlayerPrefs.GetInt("best", 0);
        bouncy = new PhysicsMaterial("bouncy") { bounciness = bounciness, dynamicFriction = 0.3f, staticFriction = 0.3f, bounceCombine = PhysicsMaterialCombine.Maximum };

        float hw = boxWidth / 2f, wallH = boxHeight + 6f;
        Wall(new Vector3(-hw - 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(hw + 0.5f, wallH / 2f, 0), new Vector3(1, wallH, 2));
        Wall(new Vector3(0, -0.5f, 0), new Vector3(boxWidth + 2, 1, 2));

        var danger = Line(new Color(1, 0.2f, 0.2f, 0.6f), 0.05f);
        danger.SetPositions(new[] { new Vector3(-hw, dangerY, -0.6f), new Vector3(hw, dangerY, -0.6f) });
        aimLine = Line(new Color(1, 1, 1, 0.35f), 0.06f);

        nextTier = RandomTier();
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

        // game over check: any settled piece poking above the danger line for 2s
        pieces.RemoveAll(p => p == null);
        bool above = false;
        foreach (var p in pieces)
            if (p != loaded && Time.time - p.spawnTime > 1.5f && p.transform.position.y + tierSizes[p.tier] * 0.5f > dangerY)
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
        loaded = Spawn(nextTier, Muzzle());
        Freeze(loaded.gameObject, true);
        nextTier = RandomTier();

        if (preview) Destroy(preview);
        preview = Instantiate(tierPrefabs[nextTier], cannon.position + new Vector3(2.2f, 0.4f, 0), Quaternion.identity);
        preview.transform.localScale = Vector3.one * 0.6f;
        Freeze(preview, true);
        Destroy(preview.GetComponent<Piece>());
    }

    void Fire()
    {
        Freeze(loaded.gameObject, false);
        loaded.spawnTime = Time.time;
        loaded.GetComponent<Rigidbody>().linearVelocity = AimDir() * shotSpeed;
        loaded = null;
        reloadAt = Time.time + reloadTime;
    }

    static void Freeze(GameObject go, bool frozen)
    {
        go.GetComponent<Rigidbody>().isKinematic = frozen;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = !frozen;
    }

    int RandomTier() => Random.Range(0, Mathf.Min(spawnableTiers, tierPrefabs.Length));
    static int Value(int tier) => 2 << tier; // 2, 4, 8, 16, 32, 64

    Piece Spawn(int tier, Vector3 pos)
    {
        var go = Instantiate(tierPrefabs[tier], pos, Quaternion.identity);
        go.transform.localScale = Vector3.one * tierSizes[tier];
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.sharedMaterial = bouncy;
        var rb = go.GetComponent<Rigidbody>();
        rb.mass = tierSizes[tier] * tierSizes[tier];
        var p = go.GetComponent<Piece>();
        p.tier = tier;
        p.game = this;
        p.spawnTime = Time.time;
        pieces.Add(p);
        return p;
    }

    public void Merge(Piece a, Piece b)
    {
        if (gameOver) return;
        a.merged = b.merged = true;
        Vector3 pos = (a.transform.position + b.transform.position) / 2f;
        Vector3 vel = (a.GetComponent<Rigidbody>().linearVelocity + b.GetComponent<Rigidbody>().linearVelocity) / 2f;
        Destroy(a.gameObject);
        Destroy(b.gameObject);

        int t = a.tier + 1;
        if (t >= tierPrefabs.Length) { score += Value(a.tier) * 4; return; } // two top-tier pieces: both vanish, bonus
        score += Value(t);
        var p = Spawn(t, pos);
        p.GetComponent<Rigidbody>().linearVelocity = vel + Vector3.up * 2f; // little hop
        StartCoroutine(Pop(p.transform, tierSizes[t]));
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
        GUI.Label(new Rect(0, 10, Screen.width, Screen.height / 10f), $"{score}   (best {best})", s);
        if (gameOver)
        {
            s.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "GAME OVER\ntap to restart", s);
        }
    }
}
