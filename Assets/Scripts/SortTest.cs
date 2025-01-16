using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SortTest : MonoBehaviour
{
    private SpatialLookup sl;
    // Start is called before the first frame update

    private ComputeBuffer _sB;
    private ComputeBuffer _iRB;
    private ComputeBuffer _p;

    public ComputeShader sortCompute;

    private int2[] getArr1;
    private int2[] getArr2;
    

    public int numP;
    
    void Start()
    {
        _sB = new ComputeBuffer(numP, sizeof(int) * 2);
        Graphics.SetRandomWriteTarget(1, _sB);
        _iRB = new ComputeBuffer(numP, sizeof(int) * 2);
        Graphics.SetRandomWriteTarget(2, _iRB);
        _p = new ComputeBuffer(numP, sizeof(float) * 2);
        sl = new SpatialLookup(numP, .5f, _p, _sB, _iRB);

        
        
        getArr1 = new int2[numP];
        getArr2 = new int2[numP];
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.Space))
        {
            float2[] p = GetRandomPoints(numP);
            _p.SetData(p);
            sl.UpdateSpatialLookupGPU();
            
        }
    }

    float2[] GetRandomPoints(int n)
    {
        float2[] points = new float2[n];
        for (int i = 0; i < n; i++)
        {
            points[i] = UnityEngine.Random.insideUnitCircle * 2;
        }
        return points;
    }
}
