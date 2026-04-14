using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxCutter
{
    public class Spinner : MonoBehaviour
    {
        public Vector3 rotationAxis = Vector3.up;
        public float rotationSpeed = 100f;

        void Update()
        {
            // Rotate around the assigned axis in local space
            transform.Rotate(rotationAxis.normalized, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}