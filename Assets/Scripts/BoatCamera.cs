using System;
using UnityEngine;

public class BoatCamera : MonoBehaviour
{
    public Rigidbody2D target;

    public Vector2 maxOffset;
    public Vector2 deadZone;

    private Vector2 _velocity;
    private Vector2 _initialPosition;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _initialPosition = transform.position;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        float dx = transform.position.x - target.position.x;
        float dy = transform.position.y - target.position.y;

        Vector3 newPos = transform.position;
        if (Math.Abs(dx) > maxOffset.x)
        {
            newPos.x = target.position.x + Mathf.Sign(dx) * maxOffset.x;
        }
        if (Math.Abs(dy) > maxOffset.y)
        {
            newPos.y = target.position.y + Mathf.Sign(dy) * maxOffset.y;
        }
        
        _velocity = (newPos - transform.position) / Time.deltaTime;
        //print("Cam: " + _velocity + ", Boat: " + target.linearVelocityX);
        transform.position = newPos;
    }

    public Vector2 GetVelocity()
    {
        return _velocity;
    }

    public Vector2 GetOffset()
    {
        return (Vector2)transform.position - _initialPosition;
    }

    

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(target.position, deadZone * 2);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(target.position, maxOffset * 2);
    }
}
