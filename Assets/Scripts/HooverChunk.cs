using BoxCutter;
using UnityEngine;

[DisallowMultipleComponent]
public class HooverChunk : MonoBehaviour
{
    [Min(1)]
    [SerializeField] private int scrapValue = 1;

    private Rigidbody cachedRigidbody;
    private BoxCutterOneDebris oneDebris;
    private bool wasCollected;

    public void Configure(int configuredScrapValue)
    {
        scrapValue = Mathf.Max(1, configuredScrapValue);
    }

    private void Awake()
    {
        cachedRigidbody = GetComponent<Rigidbody>();
        oneDebris = GetComponent<BoxCutterOneDebris>();
    }

    public void PullTowards(Vector3 collectorPosition, float pullStrength)
    {
        if (wasCollected || cachedRigidbody == null)
        {
            return;
        }

        Vector3 pullDirection = (collectorPosition - transform.position).normalized;
        cachedRigidbody.AddForce(pullDirection * pullStrength, ForceMode.Acceleration);
    }

    public bool TryCollect(ScrapWallet wallet)
    {
        if (wasCollected || wallet == null)
        {
            return false;
        }

        wasCollected = true;
        wallet.AddScrap(scrapValue);

        if (oneDebris != null)
        {
            oneDebris.ReturnToPool();
        }
        else
        {
            Destroy(gameObject);
        }

        return true;
    }
}
