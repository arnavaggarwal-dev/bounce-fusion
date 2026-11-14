using UnityEngine;

// President block with hit points. Kinematic; slides up to targetY when the rows rise.
public class Block : MonoBehaviour
{
    public int hp;
    public TextMesh label;
    [HideInInspector] public float targetY;
    public float riseSpeed = 4f;
    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        targetY = transform.position.y;
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
