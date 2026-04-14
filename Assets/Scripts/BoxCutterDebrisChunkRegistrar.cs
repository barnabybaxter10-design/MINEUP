using BoxCutter;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCutterCaller))]
public class BoxCutterDebrisChunkRegistrar : MonoBehaviour
{
    [Min(1)]
    [SerializeField] private int defaultScrapValuePerChunk = 1;

    [Min(0.5f)]
    [SerializeField] private float chunkLifetimeSeconds = 20f;

    private BoxCutterCaller caller;

    private void Awake()
    {
        caller = GetComponent<BoxCutterCaller>();
    }

    private void OnEnable()
    {
        caller.OnFragmentBatchCreated += HandleFragmentBatch;
    }

    private void OnDisable()
    {
        caller.OnFragmentBatchCreated -= HandleFragmentBatch;
    }

    private void HandleFragmentBatch(BoxCutterCaller.FragmentBatch batch)
    {
        if (batch.OneDebris == null)
        {
            return;
        }

        for (int i = 0; i < batch.OneDebris.Length; i++)
        {
            BoxCutterOneDebris oneDebris = batch.OneDebris[i];
            if (oneDebris == null)
            {
                continue;
            }

            HooverChunk chunk = oneDebris.GetComponent<HooverChunk>();
            if (chunk == null)
            {
                chunk = oneDebris.gameObject.AddComponent<HooverChunk>();
            }

            chunk.Configure(defaultScrapValuePerChunk);
            oneDebris.ReturnToPool(chunkLifetimeSeconds);
        }
    }
}
