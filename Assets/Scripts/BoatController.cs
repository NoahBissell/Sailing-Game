using UnityEngine;

public class BoatController : MonoBehaviour
{
    private Rigidbody2D rb;
    public float moveForce;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        float moveHorizontal = Input.GetAxis("Horizontal");
        rb.AddForce(Vector2.right * moveHorizontal * moveForce);
    }
}
