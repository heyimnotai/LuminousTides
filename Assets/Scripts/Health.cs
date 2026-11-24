using System;
using UnityEngine;

/// Health with an optional recharging shield. Used by the player and by enemies.
public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    public float maxShield = 0f;
    public float shieldRegenDelay = 4f;
    public float shieldRegenRate = 30f;

    public float Current { get; private set; }
    public float Shield { get; private set; }
    public bool IsDead { get; private set; }
    /// While set, nothing can hurt this (the player casting their super).
    [System.NonSerialized] public bool immune;

    public event Action<float, Vector3> Damaged;   // amount, source position
    public event Action Died;

    float lastDamageTime = -999f;

    void Awake() { ResetFull(); }

    public void ResetFull()
    {
        Current = maxHealth;
        Shield = maxShield;
        IsDead = false;
    }

    void Update()
    {
        if (IsDead || maxShield <= 0f || Shield >= maxShield) return;
        if (Time.time - lastDamageTime >= shieldRegenDelay)
            Shield = Mathf.Min(maxShield, Shield + shieldRegenRate * Time.deltaTime);
    }

    public void Damage(float amount, Vector3 sourcePosition)
    {
        if (IsDead || amount <= 0f || immune) return;
        lastDamageTime = Time.time;
        float absorbed = Mathf.Min(Shield, amount);
        Shield -= absorbed;
        Current -= amount - absorbed;
        Damaged?.Invoke(amount, sourcePosition);
        if (Current <= 0f)
        {
            Current = 0f;
            IsDead = true;
            Died?.Invoke();
        }
    }
}
