using UnityEngine;

// A target with hit points. Kinematic; slides up to targetY when the rows rise.
//  President: spins slowly about a random axis every so often.
//  Star:      patrols left/right across its row, turns steadily, shrinks as its number drops.
//  Chest:     breaking it gives a key.
public class Block : MonoBehaviour
{
    public enum Kind { President, Star, Chest }
    public Kind kind;
    public int hp;
    public TextMesh label;
    [HideInInspector] public float targetY;
    public float riseSpeed = 4f;
    public Transform model;           // the mesh; turned on its own so the label stays readable
    public Vector2 spinSpeed = new Vector2(25f, 60f);  // degrees/second, random in range
    public Vector2 spinTime = new Vector2(1.5f, 3f);   // seconds a spin lasts
    public Vector2 spinPause = new Vector2(3f, 8f);    // seconds between spins

    [Header("Star")]
    public float patrolSpeed = 1.5f;   // units/second
    [HideInInspector] public float patrolRange; // star centre moves within -range..range on X
    public float minScale = 0.45f;     // size at 1 hp, relative to full

    Rigidbody rb;
    Vector3 spinAxis;
    float speed, spinEnd, nextSpin, baseScale, patrolStart;
    int maxHp;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        targetY = transform.position.y;
        nextSpin = Time.time + Random.Range(0f, spinPause.y); // stagger so blocks don't all spin together
        patrolStart = Time.time;
    }

    void Update()
    {
        if (model == null) return;
        if (kind == Kind.Star) { model.RotateAround(transform.position, Vector3.forward, 40f * Time.deltaTime); return; }
        // every so often: a slow spin about a random axis through the block's centre
        if (Time.time >= nextSpin)
        {
            spinAxis = Random.onUnitSphere;
            speed = Random.Range(spinSpeed.x, spinSpeed.y);
            spinEnd = Time.time + Random.Range(spinTime.x, spinTime.y);
            nextSpin = spinEnd + Random.Range(spinPause.x, spinPause.y);
        }
        if (Time.time < spinEnd) model.RotateAround(transform.position, spinAxis, speed * Time.deltaTime);
    }

    void FixedUpdate()
    {
        var p = rb.position;
        float x = p.x;
        if (kind == Kind.Star && patrolRange > 0f) // starts in the middle, bounces between the walls
            x = Mathf.PingPong((Time.time - patrolStart) * patrolSpeed + patrolRange, 2f * patrolRange) - patrolRange;
        float y = Mathf.MoveTowards(p.y, targetY, riseSpeed * Time.fixedDeltaTime);
        if (x != p.x || y != p.y) rb.MovePosition(new Vector3(x, y, p.z));
    }

    public void SetHp(int value)
    {
        if (maxHp == 0) { maxHp = Mathf.Max(value, 1); baseScale = transform.localScale.x; } // first call = full size
        hp = value;
        label.text = value.ToString();
        if (kind == Kind.Star)
            transform.localScale = Vector3.one * baseScale * Mathf.Lerp(minScale, 1f, (float)hp / maxHp);
    }
}
