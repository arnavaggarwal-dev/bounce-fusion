using UnityEngine;

// One merge piece. Same-tier pieces that touch merge into the next tier.
public class Piece : MonoBehaviour
{
    public int tier;
    [HideInInspector] public MergeGame game;
    [HideInInspector] public bool merged;
    [HideInInspector] public float spawnTime;

    void OnCollisionEnter(Collision c) => TryMerge(c);
    void OnCollisionStay(Collision c) => TryMerge(c);

    void TryMerge(Collision c)
    {
        var other = c.rigidbody ? c.rigidbody.GetComponent<Piece>() : null;
        if (other == null || merged || other.merged || other.tier != tier) return;
        // only one side of the pair handles it
        if (GetInstanceID() < other.GetInstanceID()) game.Merge(this, other);
    }
}
