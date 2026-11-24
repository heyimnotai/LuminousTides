using UnityEngine;

/// Keeps something centred on the player on the ground plane (the cloud of drifting motes).
public class FollowPlayer : MonoBehaviour
{
    public float height = 3.2f;

    void LateUpdate()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.player == null) return;
        Vector3 p = gm.player.transform.position;
        transform.position = new Vector3(p.x, Mathf.Max(0f, p.y) + height, p.z);
    }
}
