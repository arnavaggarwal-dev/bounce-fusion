using UnityEngine;

// President block with hit points. Kinematic; slides up to targetY when the rows rise.
public class Block : MonoBehaviour
{
    public int hp;
    public TextMesh label;
    [HideInInspector] public float targetY;
    public float riseSpeed = 4f;
    public Transform model;           // the president mesh; spun on its own so the label stays readable
    public Vector2 spinSpeed = new Vector2(25f, 60f);  // degrees/second, random in range
    public Vector2 spinTime = new Vector2(1.5f, 3f);   // seconds a spin lasts
    public Vector2 spinPause = new Vector2(3f, 8f);    // seconds between spins
    Rigidbody rb;
    Vector3 spinAxis;
    float speed, spinEnd, nextSpin;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        targetY = transform.position.y;
        nextSpin = Time.time + Random.Range(0f, spinPause.y); // stagger so blocks don't all spin together
    }

    // every so often: a slow spin about a random axis through the block's centre
    void Update()
    {
        if (model == null) return;
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
        if (!Mathf.Approximately(p.y, targetY))
            rb.MovePosition(new Vector3(p.x, Mathf.MoveTowards(p.y, targetY, riseSpeed * Time.fixedDeltaTime), p.z));
    }

    public void SetHp(int value)
    {
        hp = value;
        label.text = value.ToString();
    }
}
