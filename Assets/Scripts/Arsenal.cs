using System.Collections;
using UnityEngine;

/// Holds the player's weapons and swaps between them: number keys or the mouse wheel.
/// Only the current weapon's component and view model are active.
public class Arsenal : MonoBehaviour
{
    public static Arsenal Instance { get; private set; }

    public Weapon[] weapons;
    public float swapTime = 0.2f;        // how long the old weapon takes to drop out of view

    public int CurrentIndex { get; private set; }
    public Weapon Current => weapons[CurrentIndex];
    public bool Swapping { get; private set; }
    /// Raised when a different weapon becomes current.
    public event System.Action<int> Switched;

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < weapons.Length; i++) SetActive(i, i == 0);
    }

    void SetActive(int index, bool active)
    {
        weapons[index].enabled = active;
        if (weapons[index].viewModel) weapons[index].viewModel.gameObject.SetActive(active);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool control = (gm == null || gm.State == GameManager.GameState.Playing) && Cursor.lockState == CursorLockMode.Locked && !InputGate.Blocked;
        if (!control || Swapping || weapons.Length < 2) return;
        for (int i = 0; i < weapons.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { Switch(i); return; }
        float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
        if (wheel > 0.01f) Switch((CurrentIndex + weapons.Length - 1) % weapons.Length);
        else if (wheel < -0.01f) Switch((CurrentIndex + 1) % weapons.Length);
    }

    public void Switch(int index)
    {
        if (index == CurrentIndex || index < 0 || index >= weapons.Length || Swapping) return;
        StartCoroutine(SwapTo(index));
    }

    IEnumerator SwapTo(int index)
    {
        Swapping = true;
        Current.Lower(swapTime + 0.05f);            // drop the old one, then bring the new one up
        yield return new WaitForSeconds(swapTime);
        SetActive(CurrentIndex, false);
        CurrentIndex = index;
        SetActive(CurrentIndex, true);
        Switched?.Invoke(index);
        yield return new WaitForSeconds(0.12f);
        Swapping = false;
    }

    /// True if any weapon has room in its reserve.
    public bool WantsAmmo()
    {
        foreach (var w in weapons) if (w.Reserve < w.maxReserve) return true;
        return false;
    }

    /// One ammo pickup tops up every weapon by its own pickup amount.
    public void AddAmmoToAll()
    {
        foreach (var w in weapons) w.AddAmmo(w.pickupRounds);
    }
}
