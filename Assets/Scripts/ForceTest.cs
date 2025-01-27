using UnityEngine;

public class ForceTest : MonoBehaviour
{
    public KeyCode key;
    private Rigidbody rb;

    public float force;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(key))
        {
            rb.AddForce(Vector3.up * force);
        }
    }
}
