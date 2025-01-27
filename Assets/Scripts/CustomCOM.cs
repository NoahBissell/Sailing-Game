using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CustomCOM : MonoBehaviour
{
    private Rigidbody2D rb;
    
    public Vector2 offset;

    private void OnValidate()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }
        
        rb.centerOfMass = offset;
    }

    private void OnDrawGizmos()
    {
        
        Gizmos.DrawSphere(rb.worldCenterOfMass, .1f);
    }
}
