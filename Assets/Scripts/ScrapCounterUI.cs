using UnityEngine;
using UnityEngine.UI;

public class ScrapCounterUI : MonoBehaviour
{
    [SerializeField] private ScrapWallet wallet;
    [SerializeField] private Text scrapText;
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
        if (scrapText == null)
        {
            return;
        }

        scrapText.text = $"{prefix}{scrapValue}";
    }
}
