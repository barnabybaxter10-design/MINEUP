using System;
using UnityEngine;

public class ScrapWallet : MonoBehaviour
{
    [SerializeField] private int startingScrap;

    public int CurrentScrap { get; private set; }

    public event Action<int> OnScrapChanged;

    private void Awake()
    {
        CurrentScrap = Mathf.Max(0, startingScrap);
    }

    public void AddScrap(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        CurrentScrap += amount;
        OnScrapChanged?.Invoke(CurrentScrap);
    }
}
