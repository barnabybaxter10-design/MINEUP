using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace BoxCutter
{
    public class PlayModeWarning : MonoBehaviour
    {
        void Start()
        {
            GetComponent<MeshRenderer>().enabled = true;
        }
    }
}