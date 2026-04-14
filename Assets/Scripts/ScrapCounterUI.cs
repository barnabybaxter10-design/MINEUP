using TMPro;
using UnityEngine;

public class ScrapCounterUI : MonoBehaviour
{
    [SerializeField] private ScrapWallet wallet;
    [SerializeField] private TMP_Text scrapText;
    [SerializeField] private TextMesh worldText;
    [SerializeField] private string prefix = "Scrap: ";

    private void Awake()
    {
        if (wallet == null)
        {
            wallet = FindObjectOfType<ScrapWallet>();
        }
    }

    private void OnEnable()
    {
        if (wallet != null)
        {
            wallet.OnScrapChanged += HandleScrapChanged;
        }

        RefreshText();
    }

    private void OnDisable()
    {
        if (wallet != null)
        {
            wallet.OnScrapChanged -= HandleScrapChanged;
        }
    }

    private void HandleScrapChanged(int currentScrap)
    {
        RefreshText(currentScrap);
    }

    public void RefreshText()
    {
        int value = wallet != null ? wallet.CurrentScrap : 0;
        RefreshText(value);
    }

    private void RefreshText(int scrapValue)
    {
        string label = $"{prefix}{scrapValue}";

        if (scrapText != null)
        {
            scrapText.text = label;
        }

        if (worldText != null)
        {
            worldText.text = label;
        }
    }
}
