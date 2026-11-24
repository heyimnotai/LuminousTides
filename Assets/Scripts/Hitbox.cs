using UnityEngine;

/// Marks a collider as a special hit zone. Damage to the owning Health is multiplied (e.g. 2 for the head).
public class Hitbox : MonoBehaviour
{
    public float multiplier = 2f;
}
