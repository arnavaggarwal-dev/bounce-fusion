using UnityEngine;

// Numbered ball. Hitting a block does damage equal to the number; two equal balls merge.
public class Ball : MonoBehaviour
{
    public int value;
    public TextMesh label;
    [HideInInspector] public MergeGame game;
    [HideInInspector] public bool merged;
    [HideInInspector] public float spawnTime;

    void OnCollisionEnter(Collision c)
    {
        var block = c.collider.GetComponentInParent<Block>();
        if (block != null) { game.HitBlock(this, block); return; }
        TryMerge(c);
    }

    void OnCollisionStay(Collision c) => TryMerge(c);

    void TryMerge(Collision c)
    {
        var other = c.rigidbody ? c.rigidbody.GetComponent<Ball>() : null;
        if (other == null || merged || other.merged || other.value != value) return;
        // only one side of the pair handles it
        if (GetInstanceID() < other.GetInstanceID()) game.Merge(this, other);
    }
}
